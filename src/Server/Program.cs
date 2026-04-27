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
builder.Services.AddSingleton<DataCatalog>(_ => EmbeddedDataLoader.LoadCatalog());
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

if (app.Environment.IsDevelopment())
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

app.MapControllers();

app.Run();

public partial class Program { }
