using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Services;
using RoguelikeCardGame.Server.Services.FileBacked;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

builder.Services.Configure<DataStorageOptions>(
    builder.Configuration.GetSection(DataStorageOptions.SectionName));

builder.Services.AddSingleton<IAccountRepository, FileAccountRepository>();
builder.Services.AddSingleton<IAudioSettingsRepository, FileAudioSettingsRepository>();
builder.Services.AddSingleton<ISaveRepository, FileSaveRepository>();

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
app.MapControllers();

app.Run();

public partial class Program { }
