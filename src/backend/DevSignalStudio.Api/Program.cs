using System.Text.Json;
using System.Text.Json.Serialization;
using DevSignalStudio.Api.Endpoints;
using DevSignalStudio.Api.Middleware;
using DevSignalStudio.Infrastructure.Configuration;
using DevSignalStudio.Infrastructure.DependencyInjection;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.ReadCommentHandling = JsonCommentHandling.Skip;
    options.SerializerOptions.AllowTrailingCommas = true;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("LocalFrontend", policy =>
    {
        policy
            .WithOrigins("http://localhost:4173", "http://127.0.0.1:4173", "http://localhost:5173", "http://127.0.0.1:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

string rootPath = DevSignalPathResolver.ResolveRoot(
    builder.Configuration["DevSignal:RootPath"],
    builder.Environment.ContentRootPath);
builder.Services.AddDevSignalStudioBackend(rootPath);

WebApplication app = builder.Build();

app.UseMiddleware<ApiExceptionMiddleware>();
app.UseCors("LocalFrontend");

await app.Services
    .GetRequiredService<DevSignalInitializer>()
    .InitializeAsync(CancellationToken.None);

app.MapGet("/", () => Results.Ok(new
{
    name = "DevSignal Studio API",
    version = "0.1.0",
    documentation = "/api/v1",
    health = "/health/ready"
}));
app.MapDevSignalEndpoints();

app.Run();

public partial class Program { }
