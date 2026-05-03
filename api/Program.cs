using Azure.Data.Tables;
using Azure.Storage.Blobs;
using BeConcrete.EmbeddingCore.Storage;
using HqAgent.Api.Middleware;
using HqAgent.Shared.Storage;
using Microsoft.Extensions.Logging;
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
        services.AddScoped<CustomerStorageService>();
        services.AddScoped<ProjectStorageService>();
        services.AddScoped<HRTableStorageService>();
        services.AddScoped<TimereportStorageService>();
        services.AddScoped<EmbeddingsStorageService>();
    })
    .Build();

host.Run();
