using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Azure.Data.Tables;
using ContractOrchestratorAgent.Services;
using HqAgent.Shared.Abstractions;
using HqAgent.Shared.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        var config = context.Configuration;

        var storageConnStr = config["STORAGE_CONNECTION_STRING"]
            ?? throw new InvalidOperationException("STORAGE_CONNECTION_STRING is not configured");

        services.AddSingleton(new BlobServiceClient(storageConnStr));
        services.AddSingleton(new TableServiceClient(storageConnStr));
        services.AddSingleton(new QueueServiceClient(storageConnStr));

        services.AddHttpClient<IAIModelClient, AnthropicHttpClient>();

        services.AddScoped<BlobStorageService>();
        services.AddScoped<ContractAnalysisService>();
        services.AddScoped<TableStorageService>();
        services.AddScoped<ContractProcessor>();
    })
    .Build();

host.Run();
