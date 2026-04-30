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

    /// <summary>
    /// Returns customers whose name fuzzy-matches any of the supplied counterparty names,
    /// after filtering out "Be Concrete" variants (the company itself).
    /// Matching: customer name is a substring of the counterparty string, or vice versa.
    /// </summary>
    public async Task<List<CustomerEntity>> MatchByCounterpartiesAsync(
        IEnumerable<string> counterpartyNames, CancellationToken ct = default)
    {
        var candidates = counterpartyNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Where(n => !n.Contains("be concrete", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count == 0) return [];

        var allCustomers = await ListCustomersAsync(includeInactive: true, ct: ct);
        var matched = new List<CustomerEntity>();

        foreach (var customer in allCustomers)
        {
            var customerName = customer.Name;
            if (candidates.Any(c =>
                customerName.Contains(c, StringComparison.OrdinalIgnoreCase) ||
                c.Contains(customerName, StringComparison.OrdinalIgnoreCase)))
            {
                matched.Add(customer);
            }
        }

        return matched;
    }

    /// <summary>
    /// Idempotently appends contractId to the customer's LinkedContractIds JSON array.
    /// </summary>
    public async Task LinkContractAsync(string customerId, string contractId, CancellationToken ct = default)
    {
        var customer = await GetCustomerAsync(customerId, ct);
        if (customer is null) return;

        List<string> ids;
        try { ids = System.Text.Json.JsonSerializer.Deserialize<List<string>>(customer.LinkedContractIds) ?? []; }
        catch { ids = []; }

        if (ids.Contains(contractId, StringComparer.OrdinalIgnoreCase)) return;

        ids.Add(contractId);
        customer.LinkedContractIds = System.Text.Json.JsonSerializer.Serialize(ids);

        var table = _client.GetTableClient(TableNames.Customers);
        await table.UpsertEntityAsync(customer, TableUpdateMode.Replace, ct);
        _logger.LogInformation("Linked contract {ContractId} to customer {CustomerId}", contractId, customerId);
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
