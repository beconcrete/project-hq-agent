using Anthropic;
using Anthropic.Core;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using HqAgent.Functions.Services;
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

        var anthropicApiKey = config["ANTHROPIC_API_KEY"]
            ?? throw new InvalidOperationException("ANTHROPIC_API_KEY is not configured");

        services.AddSingleton(new BlobServiceClient(storageConnection));
        services.AddSingleton(new TableServiceClient(storageConnection));

        services.AddSingleton<IAnthropicClient>(
            new AnthropicClient(new ClientOptions { ApiKey = anthropicApiKey }));

        services.AddSingleton<BlobStorageService>();
        services.AddSingleton<TableStorageService>();
        services.AddSingleton<ContractWorkflow>();
    })
    .Build();

host.Run();
