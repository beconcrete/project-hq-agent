using Anthropic;
using Anthropic.Core;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using HqAgent.Functions.Services;
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

        var storageConnection = config["STORAGE_CONNECTION_STRING"]
            ?? throw new InvalidOperationException("STORAGE_CONNECTION_STRING is not configured");

        var anthropicApiKey = config["ANTHROPIC_API_KEY"]
            ?? throw new InvalidOperationException("ANTHROPIC_API_KEY is not configured");

        services.AddSingleton(new BlobServiceClient(storageConnection));

        services.AddSingleton(sp =>
        {
            var tableClient = new TableClient(storageConnection, "ContractExtractions");
            tableClient.CreateIfNotExists();
            return tableClient;
        });

        services.AddSingleton<IAnthropicClient>(
            new AnthropicClient(new ClientOptions { ApiKey = anthropicApiKey }));

        services.AddSingleton<ContractWorkflow>();
        services.AddSingleton<ExtractionTableWriter>();
    })
    .Build();

host.Run();
