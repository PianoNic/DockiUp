using DockiUp.API.Nodes;
using DockiUp.API.SignalR;
using DockiUp.Application;
using DockiUp.Application.Interfaces;
using DockiUp.Application.Models;
using DockiUp.Infrastructure;
using DockiUp.Infrastructure.Clients;
using DockiUp.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// DockiUp runs in one of two roles from the same image. When Node:ServerUrl is set it boots as a
// stripped "node": it dials OUT to the control plane over SignalR and runs Docker work on its own host.
// No UI, no app database, no auth, no user-facing endpoints - only /health. Anything else is the full
// control plane.
if (!string.IsNullOrWhiteSpace(builder.Configuration["Node:ServerUrl"]))
{
    builder.Services.Configure<SystemPaths>(builder.Configuration.GetSection("SystemPaths"));
    builder.Services.AddSingleton<IDockiUpDockerClient, DockiUpDockerClient>();
    builder.Services.AddSingleton<IContainerExecRegistry, ContainerExecRegistry>();
    builder.Services.AddScoped<IDockerService, DockerService>();
    builder.Services.AddScoped<IDockiUpProjectConfigurationService, DockiUpProjectConfigurationService>();
    // DockerService takes the DbContext, but a node only calls its raw (DB-free) paths, so the context
    // is registered without a connection string and never opened.
    builder.Services.AddDbContext<DockiUpDbContext>(options => options.UseNpgsql());
    builder.Services.AddScoped<IDockiUpDbContext>(provider => provider.GetRequiredService<DockiUpDbContext>());
    builder.Services.AddHostedService<NodeAgentHostedService>();

    var nodeApp = builder.Build();
    nodeApp.MapGet("/health", () => Results.Ok(new { status = "ok", role = "node" }));
    nodeApp.Run();
    return;
}

// Auth is opt-in: only when Oidc:Authority is configured does Toamaisutaa validate tokens and lock the API down.
// Unset (the default, and the dev experience) leaves every endpoint anonymous - open mode.
var authEnabled = !string.IsNullOrWhiteSpace(builder.Configuration["Oidc:Authority"]);

#region Configure Services
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddOpenApi();

builder.Services.AddApplicationModule();

builder.Services.Configure<SystemPaths>(builder.Configuration.GetSection("SystemPaths"));
// Deploy pipeline: one queue (hosted worker) runs every deployment; periodic checks feed it.
builder.Services.AddScoped<DockiUp.Application.Deployments.DeploymentRunner>();
builder.Services.AddSingleton<DockiUp.Application.Deployments.IDeploymentEvents, DockiUp.API.Deployments.DeploymentEvents>();
builder.Services.AddSingleton<DockiUp.API.Deployments.DeploymentQueue>();
builder.Services.AddSingleton<DockiUp.Application.Deployments.IDeploymentQueue>(sp => sp.GetRequiredService<DockiUp.API.Deployments.DeploymentQueue>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<DockiUp.API.Deployments.DeploymentQueue>());
builder.Services.AddHostedService<DockiUp.API.HostedServices.PeriodicUpdateHostedService>();
builder.Services.AddHostedService<DockiUp.API.HostedServices.ContainerStateBroadcastHostedService>();

builder.Services.AddSignalR();
builder.Services.AddSingleton<IDockiUpHubBroadcastService, DockiUpHubBroadcastService>();
builder.Services.AddSingleton<DockiUp.API.SignalR.IExecRelay, DockiUp.API.SignalR.ExecRelay>();

builder.Services.AddScoped<IDockerService, DockerService>();
builder.Services.AddScoped<IActivityLogger, ActivityLogger>();
builder.Services.AddScoped<ISecretsVaultService, SecretsVaultService>();
builder.Services.AddSingleton<ISecretGeneratorService, SecretGeneratorService>();
builder.Services.AddSingleton<IContainerExecRegistry, ContainerExecRegistry>();
builder.Services.AddScoped<IDockiUpProjectConfigurationService, DockiUpProjectConfigurationService>();
builder.Services.AddSingleton<IDockiUpDockerClient, DockiUpDockerClient>();

#region Multi-server nodes
// Live-connection registry is shared by the hub (control plane) and the controller.
builder.Services.AddSingleton<INodeRegistry, NodeRegistry>();

// Control plane: route Docker work to the local daemon or a node over SignalR, by the project's NodeId.
builder.Services.AddScoped<IDockerServiceResolver, DockerServiceResolver>();
builder.Services.AddSingleton<DeployLogRelay>();
builder.Services.AddSingleton<INodeRpc, NodeRpc>();
builder.Services.AddSingleton<INodeDirectory, NodeDirectory>();
// Project files read from / downloaded off a node come back as one message (default limit 32 KB);
// nodes are token-authenticated, so allow up to the 25 MB file limit (base64) plus envelope.
builder.Services.Configure<Microsoft.AspNetCore.SignalR.HubOptions<NodeHub>>(o => o.MaximumReceiveMessageSize = 40 * 1024 * 1024);
#endregion

#region Notifications, project secrets, git credentials (#75-#77)
// A plain HttpClient (not IHttpClientFactory): the factory's handlers log request URLs, and webhook /
// Telegram URLs carry the channel's credential.
builder.Services.AddSingleton<DockiUp.Application.Notifications.INotificationSender>(_ =>
    new NotificationSender(new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })));
builder.Services.AddSingleton<DockiUp.API.Notifications.NotificationDispatcher>();
builder.Services.AddSingleton<DockiUp.Application.Notifications.INotificationDispatcher>(sp => sp.GetRequiredService<DockiUp.API.Notifications.NotificationDispatcher>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<DockiUp.API.Notifications.NotificationDispatcher>());
builder.Services.AddScoped<DockiUp.Application.Git.IGitCredentialsProvider, DockiUp.Application.Git.GitCredentialsProvider>();
#endregion

#region Image updates (#68/#69/#70)
// Registry client for digest checks and tag lists; anonymous unless another credentials provider is registered.
builder.Services.AddHttpClient<IRegistryClient, RegistryClient>(http => http.Timeout = TimeSpan.FromSeconds(20));
Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions
    .TryAddSingleton<IRegistryCredentialsProvider, AnonymousRegistryCredentials>(builder.Services);
builder.Services.AddSingleton<DockiUp.Application.ImageUpdates.IImageUpdateEvents, DockiUp.API.HostedServices.ImageUpdateEvents>();
builder.Services.AddHostedService<DockiUp.API.HostedServices.ImageUpdateHostedService>();
#endregion
#endregion

#region Database Configuration
builder.Services.AddDbContext<DockiUpDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DockiUpDatabase"),
        npgsqlOptions => npgsqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorCodesToAdd: null)));

builder.Services.AddScoped<IDockiUpDbContext>(provider =>
    provider.GetRequiredService<DockiUpDbContext>());
#endregion

#region Error responses
// Handlers signal "no such thing" / "bad input" with plain exceptions; surface those as 404/400
// ProblemDetails carrying the message, so the UI can show *why* instead of a bare 500.
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    var exception = context.Exception
        ?? context.HttpContext.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    if (exception is KeyNotFoundException or ArgumentException)
        context.ProblemDetails.Detail = exception.Message;
});
#endregion

#region CORS Configuration
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(corsBuilder =>
    {
        var allowedOrigins = Environment.GetEnvironmentVariable("CORS_ALLOWED_ORIGINS")
            ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? new[] { "http://localhost:4200" };

        corsBuilder.WithOrigins(allowedOrigins);
        corsBuilder.WithExposedHeaders("Content-Disposition");
        corsBuilder.AllowAnyHeader();
        corsBuilder.AllowAnyMethod();
        corsBuilder.AllowCredentials();
    });
});
#endregion

#region Authentication / Authorization
// Toamaisutaa validates the IdP's access tokens (config section "Oidc") and puts every endpoint behind
// an authenticated-user fallback policy; [AllowAnonymous] opts out (GetAppInfo, webhook, node hub, SPA shell).
// Without an authority nothing is registered and the app runs open, as in dev and the test suite.
if (authEnabled)
{
    builder.Services.AddToamaisutaaBearer(builder.Configuration);
    builder.Services.AddToamaisutaaAuthorization(builder.Configuration);
    builder.Services.AddToamaisutaaCurrentUser(); // who did it, for the activity feed
}
#endregion

var app = builder.Build();

if (app.Environment.IsProduction())
{
    var corsOrigins = Environment.GetEnvironmentVariable("CORS_ALLOWED_ORIGINS");
    if (string.IsNullOrWhiteSpace(corsOrigins) || corsOrigins.Trim() == "http://localhost:4200")
        app.Services.GetRequiredService<ILogger<Program>>().LogWarning("CORS may be using default origin in production. Set CORS_ALLOWED_ORIGINS.");
}

#region Database Initialization with Retry Logic
bool dbConnected = false;
int retryCount = 0;
const int maxRetries = 10;
const int retryDelaySeconds = 5;
var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();

while (!dbConnected && retryCount < maxRetries)
{
    try
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DockiUpDbContext>();
        startupLogger.LogInformation("Attempting database connection and migrations (Attempt {Attempt}/{MaxRetries})...", retryCount + 1, maxRetries);
        dbContext.Database.Migrate();
        dbConnected = true;
        startupLogger.LogInformation("Database connection successful and migrations applied.");
    }
    catch (NpgsqlException ex)
    {
        startupLogger.LogError(ex, "Database connection failed: {ErrorMessage}", ex.Message);
        retryCount++;
        if (retryCount < maxRetries)
        {
            startupLogger.LogInformation("Retrying in {Delay} seconds...", retryDelaySeconds);
            Thread.Sleep(TimeSpan.FromSeconds(retryDelaySeconds));
        }
        else
        {
            startupLogger.LogCritical("Failed to connect to the database after {MaxRetries} retries. Application will terminate.", maxRetries);
            throw;
        }
    }
    catch (Exception ex)
    {
        startupLogger.LogError(ex, "Unexpected error during database setup: {ErrorMessage}", ex.Message);
        retryCount++;
        if (retryCount < maxRetries)
        {
            startupLogger.LogInformation("Retrying in {Delay} seconds...", retryDelaySeconds);
            Thread.Sleep(TimeSpan.FromSeconds(retryDelaySeconds));
        }
        else
        {
            startupLogger.LogCritical("Database operations failed after {MaxRetries} retries.", maxRetries);
            throw;
        }
    }
}
#endregion

#region Configure HTTP Pipeline
if (app.Environment.IsDevelopment())
    app.MapOpenApi();

if (!app.Environment.IsProduction())
    app.UseHttpsRedirection();

app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = exception => exception switch
    {
        KeyNotFoundException => StatusCodes.Status404NotFound,
        ArgumentException => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status500InternalServerError,
    },
});
app.UseStaticFiles();
app.UseRouting();
app.UseCors();
if (authEnabled)
    app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<DockiUpHub>("/hubs/dockiup");
app.MapHub<NodeHub>("/hubs/node");

// The SPA shell must load even when unauthenticated so it can run the client-side login redirect.
app.MapFallbackToFile("index.html").AllowAnonymous();
#endregion

app.Run();
