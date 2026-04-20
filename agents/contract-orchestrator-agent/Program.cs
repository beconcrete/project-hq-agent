using Azure.Data.Tables;
using Azure.Storage.Blobs;
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
        services.AddHttpClient();

        var config = context.Configuration;

        var storageConnection = config["STORAGE_CONNECTION_STRING"]
            ?? throw new InvalidOperationException("STORAGE_CONNECTION_STRING is not configured");

        services.AddSingleton(new BlobServiceClient(storageConnection));
        services.AddSingleton(new TableServiceClient(storageConnection));
        services.AddSingleton<BlobStorageService>();
        services.AddSingleton<TableStorageService>();

        services.AddSingleton<IContractAnalysisWorkflow, OpenAIContractWorkflow>();
    })
    .Build();

host.Run();
