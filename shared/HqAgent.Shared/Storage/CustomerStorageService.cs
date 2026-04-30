using Azure;
using Azure.Data.Tables;
using HqAgent.Shared.Models;
using Microsoft.Extensions.Logging;

namespace HqAgent.Shared.Storage;

public class CustomerStorageService
{
    private readonly TableServiceClient _client;
    private readonly ILogger<CustomerStorageService> _logger;

    public CustomerStorageService(TableServiceClient client, ILogger<CustomerStorageService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<List<CustomerEntity>> ListCustomersAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        var table = _client.GetTableClient(TableNames.Customers);
        await table.CreateIfNotExistsAsync(ct);

        var results = new List<CustomerEntity>();
        await foreach (var entity in table.QueryAsync<CustomerEntity>(
            filter: "PartitionKey eq 'customers'", cancellationToken: ct))
        {
            if (!includeInactive && entity.Status == "inactive")
                continue;
            results.Add(entity);
        }

        return results.OrderBy(c => c.Name).ToList();
    }

    public async Task<CustomerEntity?> GetCustomerAsync(string customerId, CancellationToken ct = default)
    {
        var table = _client.GetTableClient(TableNames.Customers);
        await table.CreateIfNotExistsAsync(ct);

        try
        {
            var response = await table.GetEntityAsync<CustomerEntity>("customers", customerId, cancellationToken: ct);
            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<CustomerEntity?> FindByNameAsync(string name, CancellationToken ct = default)
    {
        var customers = await ListCustomersAsync(includeInactive: true, ct: ct);
        return customers.FirstOrDefault(c =>
            c.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    public async Task WriteCustomerAsync(CustomerEntity entity, CancellationToken ct = default)
    {
        var table = _client.GetTableClient(TableNames.Customers);
        await table.CreateIfNotExistsAsync(ct);

        entity.PartitionKey = "customers";
        if (string.IsNullOrWhiteSpace(entity.RowKey))
            entity.RowKey = Guid.NewGuid().ToString();

        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
        _logger.LogInformation("Wrote customer {CustomerId} ({Name})", entity.RowKey, entity.Name);
    }
}
