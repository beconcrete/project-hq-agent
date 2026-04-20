using Azure.Data.Tables;
using Azure.Storage.Blobs;
using HqAgent.Api.Middleware;
using HqAgent.Shared.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults(app =>
    {
        app.UseMiddleware<RequireAccessMiddleware>();
    })
    .ConfigureServices((ctx, services) =>
    {
        services.AddHttpClient();
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        var connStr = ctx.Configuration["STORAGE_CONNECTION_STRING"]
            ?? ctx.Configuration["AzureWebJobsStorage"]
            ?? "";
        services.AddSingleton(new TableServiceClient(connStr));
        services.AddSingleton(new BlobServiceClient(connStr));
        services.AddScoped<TableStorageService>();
        services.AddScoped<BlobStorageService>();
    })
    .Build();

host.Run();
