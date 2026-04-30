using ComfyBridge.Api.Middleware;
using ComfyBridge.Api.Workers;
using ComfyBridge.Application.DependencyInjection;
using ComfyBridge.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });
builder.Services.AddRazorPages();
builder.Services.AddOpenApi();

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

builder.Services.AddHostedService<GenerationJobWorker>();

var app = builder.Build();

var runningInContainer = string.Equals(
    Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
    "true",
    StringComparison.OrdinalIgnoreCase);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<ApiExceptionMiddleware>();
if (!runningInContainer)
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();
app.MapControllers();
app.MapRazorPages();

app.Run();
