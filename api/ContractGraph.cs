using System.Net;
using System.Text.Json;
using HqAgent.Shared.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;

namespace HqAgent.Api;

/// <summary>
/// Returns a Cytoscape-ready graph of all contracts grouped by signing party.
/// GET /api/contract-graph
/// Root node → party nodes → contract leaf nodes.
/// Contracts with no party land under a synthetic "Unknown" node.
/// Requires admin role.
/// </summary>
public class ContractGraph
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly TableStorageService _table;
    private readonly string _appId;

    public ContractGraph(IHttpClientFactory httpFactory, TableStorageService table, IConfiguration config)
    {
        _httpFactory = httpFactory;
        _table       = table;
        _appId       = config["APP_ID"] ?? "hqagents";
    }

    [Function("ContractGraph")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "contract-graph")] HttpRequestData req,
        FunctionContext context)
    {
        RoleGuard guard;
        try { guard = await RoleGuard.CheckAsync(req, _httpFactory, _appId, Roles.Admin); }
        catch { return await PlainResponse(req, HttpStatusCode.ServiceUnavailable, "Auth service unavailable"); }

        if (!guard.Allowed)
            return await PlainResponse(req, HttpStatusCode.Forbidden, "Forbidden");

        var entities = await _table.ListExtractionsAsync();

        var nodes = new List<object>();
        var edges = new List<object>();

        // Root node
        nodes.Add(new
        {
            id    = "__root__",
            type  = "root",
            label = "Be Concrete / Björn Eriksen",
        });

        // Group contracts by effective party
        var partyContracts = new Dictionary<string, List<HqAgent.Shared.Models.ContractExtractionEntity>>(StringComparer.Ordinal);
        foreach (var entity in entities)
        {
            var party = EffectiveParty(entity);
            if (!partyContracts.TryGetValue(party, out var list))
            {
                list = [];
                partyContracts[party] = list;
            }
            list.Add(entity);
        }

        // Emit party nodes, then contract leaf nodes
        foreach (var (partyName, contracts) in partyContracts)
        {
            var partyId = $"party:{partyName}";

            var people = contracts
                .SelectMany(e => ParseJsonList(e.PeopleMentioned))
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p)
                .ToList();

            nodes.Add(new
            {
                id    = partyId,
                type  = partyName == "__unknown__" ? "party" : "party",
                label = partyName == "__unknown__" ? "Unknown" : partyName,
                people,
            });

            edges.Add(new
            {
                id     = $"e-root-{partyId}",
                source = "__root__",
                target = partyId,
            });

            foreach (var entity in contracts)
            {
                var contractId = $"contract:{entity.PartitionKey}";
                nodes.Add(new
                {
                    id                  = contractId,
                    type                = "contract",
                    label               = ContractLabel(entity),
                    partyId             = partyId,
                    rowKey              = entity.PartitionKey,
                    documentType        = entity.DocumentType,
                    counterparty        = entity.PrimaryCounterparty,
                    expiryDate          = entity.ExpiryDate.HasValue
                                          ? entity.ExpiryDate.Value.ToString("yyyy-MM-dd")
                                          : (string?)null,
                    renewalStatus       = RenewalStatus(entity),
                    reviewState         = entity.ReviewState,
                    fileName            = entity.FileName,
                    manualPartyOverride = entity.ManualPartyOverride,
                });

                edges.Add(new
                {
                    id     = $"e-{partyId}-{contractId}",
                    source = partyId,
                    target = contractId,
                });
            }
        }

        var payload = new { nodes, edges };
        var res = req.CreateResponse();
        await res.WriteAsJsonAsync(payload);
        res.StatusCode = HttpStatusCode.OK;
        return res;
    }

    // Names that represent "us" — should never appear as party nodes
    private static readonly HashSet<string> OwnNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "be concrete ab", "be concrete", "björn eriksen", "bjorn eriksen",
    };

    private static bool IsOwnEntity(string name) =>
        !string.IsNullOrWhiteSpace(name) && OwnNames.Contains(name.Trim());

    private static string EffectiveParty(HqAgent.Shared.Models.ContractExtractionEntity entity)
    {
        if (!string.IsNullOrWhiteSpace(entity.ManualPartyOverride))
            return entity.ManualPartyOverride;

        // Use PrimaryCounterparty unless it's our own company/person
        if (!string.IsNullOrWhiteSpace(entity.PrimaryCounterparty) &&
            !IsOwnEntity(entity.PrimaryCounterparty))
            return entity.PrimaryCounterparty;

        // Fall back to first non-own name in the counterparty array
        var others = ParseJsonList(entity.CounterpartyNames)
            .Where(n => !string.IsNullOrWhiteSpace(n) && !IsOwnEntity(n))
            .ToList();

        return others.Count > 0 ? others[0] : "__unknown__";
    }

    private static string ContractLabel(HqAgent.Shared.Models.ContractExtractionEntity entity)
    {
        if (!string.IsNullOrWhiteSpace(entity.DocumentType))
            return entity.DocumentType;
        if (!string.IsNullOrWhiteSpace(entity.FileName))
            return entity.FileName;
        return "Contract";
    }

    private static string? RenewalStatus(HqAgent.Shared.Models.ContractExtractionEntity entity) =>
        entity.AutoRenewal switch
        {
            true  => "Auto-renews",
            false => "No auto-renewal",
            null  => null,
        };

    private static IReadOnlyList<string> ParseJsonList(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    private static async Task<HttpResponseData> PlainResponse(
        HttpRequestData req, HttpStatusCode status, string message)
    {
        var res = req.CreateResponse();
        await res.WriteStringAsync(message);
        res.StatusCode = status;
        return res;
    }
}
