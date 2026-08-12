using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Services;
using RoguelikeCardGame.Server.Services.FileBacked;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

builder.Services.Configure<DataStorageOptions>(
    builder.Configuration.GetSection(DataStorageOptions.SectionName));

builder.Services.AddSingleton<IAccountRepository, FileAccountRepository>();
builder.Services.AddSingleton<IAudioSettingsRepository, FileAudioSettingsRepository>();
builder.Services.AddSingleton<ISaveRepository, FileSaveRepository>();
builder.Services.AddSingleton<IHistoryRepository, FileHistoryRepository>();
builder.Services.AddSingleton<IBestiaryRepository, FileBestiaryRepository>();

builder.Services.AddSingleton<MapGenerationConfig>(_ => MapGenerationConfigLoader.LoadAct1());
builder.Services.AddSingleton<IDungeonMapGenerator, DungeonMapGenerator>();
// Phase 10.5.J: DataCatalog をミュータブルに保つため Provider 経由に差し替え。
// Provider 自体は Singleton。controller 側は DataCatalog を直 inject しているので、
// Transient で都度 Provider.Current を返すと controller は無修正のまま最新 catalog を見る。
builder.Services.AddSingleton<DataCatalogProvider>();
builder.Services.AddTransient<DataCatalog>(sp =>
    sp.GetRequiredService<DataCatalogProvider>().Current);

// DevCardWriter: override / base / backup の disk I/O を担うヘルパ (DEV 専用)。
// 本番環境でも DI に登録するが、controller 側で IsDevelopment ガードしているので呼ばれない。
builder.Services.AddSingleton<DevCardWriter>(sp =>
{
    var env = sp.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();
    var repoRoot = Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", ".."));
    var overrideRoot = Path.Combine(repoRoot, "data-local", "dev-overrides");
    var baseCardsDir = Path.Combine(repoRoot, "src", "Core", "Data", "Cards");
    var backupRoot = Path.Combine(repoRoot, "data-local", "backups");
    return new DevCardWriter(overrideRoot, baseCardsDir, backupRoot);
});
// DevRelicWriter: relic 用 (Phase 10.5.L1)。card と並列に override/base/backup を扱う。
builder.Services.AddSingleton<DevRelicWriter>(sp =>
{
    var env = sp.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();
    var repoRoot = Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", ".."));
    var overrideRoot = Path.Combine(repoRoot, "data-local", "dev-overrides");
    var baseRelicsDir = Path.Combine(repoRoot, "src", "Core", "Data", "Relics");
    var backupRoot = Path.Combine(repoRoot, "data-local", "backups");
    return new DevRelicWriter(overrideRoot, baseRelicsDir, backupRoot);
});
builder.Services.AddSingleton<RunStartService>();
builder.Services.AddSingleton<BattleSessionStore>();

const string CorsPolicyName = "ClientCors";
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("E2E"))
{
    app.MapOpenApi();
}

app.UseCors(CorsPolicyName);
app.UseExceptionHandler();
app.UseStatusCodePages();

// パス区切り文字を URL エンコードで迂回する試行 (%2F / %5C) を 400 で拒否する。
// ルーティングは %2F を '/' に復号してセグメント分割するため、事前に rawPath で判定する。
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? string.Empty;
    if (path.Contains("%2F", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("%5C", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("エンコードされたパス区切り文字は許可されていません。");
        return;
    }
    await next();
});

// Resolve the stored account first. Besides keeping filesystem-backed repositories portable
// across case-sensitive and case-insensitive hosts, this prevents arbitrary unknown header
// values from permanently allocating entries in BattleSessionStore's command-gate table.
// Run state and battle state are both read-modify-write resources, so every mutation for the
// same canonical account is serialized.
app.Use(async (context, next) =>
{
    var request = context.Request;
    bool isApiMutation = request.Path.StartsWithSegments(
            new PathString("/api/v1"), StringComparison.OrdinalIgnoreCase)
        && (HttpMethods.IsPost(request.Method)
            || HttpMethods.IsPut(request.Method)
            || HttpMethods.IsPatch(request.Method)
            || HttpMethods.IsDelete(request.Method));

    if (!request.Path.StartsWithSegments(
            new PathString("/api/v1"), StringComparison.OrdinalIgnoreCase)
        || !request.Headers.TryGetValue("X-Account-Id", out var rawAccountId))
    {
        await next();
        return;
    }

    // Controllers consistently use the first header value; do the same so an additional
    // header value cannot make middleware and controller resolve different identities.
    var accountId = rawAccountId.FirstOrDefault();
    if (string.IsNullOrWhiteSpace(accountId))
    {
        await next();
        return;
    }

    Account? account;
    try
    {
        var accounts = context.RequestServices.GetRequiredService<IAccountRepository>();
        account = await accounts.GetAsync(accountId, context.RequestAborted);
    }
    catch (ArgumentException)
    {
        // Invalid values remain the controller's responsibility so its existing
        // ProblemDetails/status stays unchanged.
        await next();
        return;
    }

    if (account is null)
    {
        bool isAccountCreation = HttpMethods.IsPost(request.Method)
            && request.Path.Equals(
                new PathString("/api/v1/accounts"), StringComparison.OrdinalIgnoreCase);
        if (isApiMutation && !isAccountCreation)
        {
            // Do not let an unknown-ID mutation race account creation between this lookup
            // and a controller lookup. Account creation itself has its own repository gate.
            await Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: $"アカウントが見つかりません: {accountId}")
                .ExecuteAsync(context);
            return;
        }

        // Reads and account creation keep their endpoint-specific response behavior.
        await next();
        return;
    }

    // Downstream repositories use account IDs as filenames. Always give them the exact stored
    // spelling so Alice/alice behaves identically on Linux and Windows.
    request.Headers["X-Account-Id"] = account.Id;

    if (!isApiMutation)
    {
        await next();
        return;
    }

    var sessions = context.RequestServices.GetRequiredService<BattleSessionStore>();
    await using var commandLock = await sessions.AcquireCommandLockAsync(
        account.Id, context.RequestAborted);
    await next();
});

app.MapControllers();

app.Run();

public partial class Program { }
