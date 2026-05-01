using System.Net;
using System.Text.Json;
using HqAgent.Shared.Models;
using HqAgent.Shared.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;

namespace HqAgent.Api;

/// <summary>
/// Returns a Cytoscape-ready hierarchical graph of the company entity map.
/// GET /api/hq-graph
/// Topology: Be Concrete (root) → Customers → Contracts + Projects per customer
///           Be Concrete (root) → Info → Employees
/// Requires admin role.
/// </summary>
public class HqGraph
{
    private readonly IHttpClientFactory    _httpFactory;
    private readonly TableStorageService   _table;
    private readonly CustomerStorageService _customers;
    private readonly ProjectStorageService  _projects;
    private readonly HRTableStorageService  _hr;
    private readonly string _appId;

    public HqGraph(
        IHttpClientFactory    httpFactory,
        TableStorageService   table,
        CustomerStorageService customers,
        ProjectStorageService  projects,
        HRTableStorageService  hr,
        IConfiguration config)
    {
        _httpFactory = httpFactory;
        _table       = table;
        _customers   = customers;
        _projects    = projects;
        _hr          = hr;
        _appId       = config["APP_ID"] ?? "hqagents";
    }

    [Function("HqGraph")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "hq-graph")] HttpRequestData req,
        FunctionContext context)
    {
        RoleGuard guard;
        try { guard = await RoleGuard.CheckAsync(req, _httpFactory, _appId, Roles.Admin); }
        catch { return await PlainResponse(req, HttpStatusCode.ServiceUnavailable, "Auth service unavailable"); }

        if (!guard.Allowed)
            return await PlainResponse(req, HttpStatusCode.Forbidden, "Forbidden");

        var ct = context.CancellationToken;

        var contractsTask = _table.ListExtractionsAsync();
        var customersTask = _customers.ListCustomersAsync(includeInactive: false, ct: ct);
        var projectsTask  = _projects.ListProjectsAsync(includeClosedProjects: true, ct: ct);
        var employeesTask = _hr.ListEmployeesAsync(includeOffboarded: false, ct: ct);
        await Task.WhenAll(contractsTask, customersTask, projectsTask, employeesTask);

        var contracts = contractsTask.Result;
        var customers = customersTask.Result;
        var projects  = projectsTask.Result;
        var employees = employeesTask.Result;

        var nodes = new List<object>();
        var edges = new List<object>();

        nodes.Add(new { id = "__root__", type = "root", label = "Be Concrete AB" });

        // Info node + employees
        nodes.Add(new { id = "__info__", type = "info", label = "Info" });
        edges.Add(new { id = "e-root-info", source = "__root__", target = "__info__" });

        foreach (var emp in employees)
        {
            var empId = $"employee:{emp.RowKey}";
            nodes.Add(new
            {
                id         = empId,
                type       = "employee",
                label      = emp.FullName,
                email      = emp.RowKey,
                status     = emp.Status,
                parentId   = "__info__",
            });
            edges.Add(new { id = $"e-info-{empId}", source = "__info__", target = empId });
        }

        // Group contracts by linked customer
        var contractsByCustomer = new Dictionary<string, List<ContractEntity>>(StringComparer.Ordinal);
        var unlinked = new List<ContractEntity>();

        foreach (var contract in contracts)
        {
            var customerIds = ParseJsonList(contract.LinkedCustomerIds);
            if (customerIds.Count == 0)
            {
                unlinked.Add(contract);
                continue;
            }
            foreach (var cid in customerIds)
            {
                if (!contractsByCustomer.TryGetValue(cid, out var list))
                {
                    list = [];
                    contractsByCustomer[cid] = list;
                }
                list.Add(contract);
            }
        }

        // Group projects by customer
        var projectsByCustomer = new Dictionary<string, List<ProjectEntity>>(StringComparer.Ordinal);
        foreach (var proj in projects)
        {
            if (string.IsNullOrWhiteSpace(proj.CustomerId)) continue;
            if (!projectsByCustomer.TryGetValue(proj.CustomerId, out var list))
            {
                list = [];
                projectsByCustomer[proj.CustomerId] = list;
            }
            list.Add(proj);
        }

        // Customer nodes + their children
        foreach (var customer in customers)
        {
            var customerId = $"customer:{customer.RowKey}";
            var custContracts = contractsByCustomer.GetValueOrDefault(customer.RowKey, []);
            var custProjects  = projectsByCustomer.GetValueOrDefault(customer.RowKey, []);

            nodes.Add(new
            {
                id            = customerId,
                type          = "customer",
                label         = customer.Name,
                customerId    = customer.RowKey,
                contractCount = custContracts.Count,
                projectCount  = custProjects.Count,
                orgNumber     = customer.OrgNumber,
                status        = customer.Status,
            });
            edges.Add(new { id = $"e-root-{customerId}", source = "__root__", target = customerId });

            foreach (var contract in custContracts)
            {
                var contractNodeId = $"contract:{contract.RowKey}:{customer.RowKey}";
                nodes.Add(new
                {
                    id               = contractNodeId,
                    type             = "contract",
                    label            = ContractLabel(contract),
                    parentId         = customerId,
                    contractId       = contract.RowKey,
                    documentType     = contract.DocumentType,
                    expiryDate       = contract.ExpiryDate?.ToString("yyyy-MM-dd"),
                    reviewState      = contract.ReviewState,
                    fileName         = contract.FileName,
                    status           = contract.Status,
                    counterparty     = contract.PrimaryCounterparty,
                    noticePeriodDays = contract.NoticePeriodDays,
                    noticeDeadline   = contract.NoticeDeadline?.ToString("yyyy-MM-dd"),
                    autoRenewal      = contract.AutoRenewal,
                    paymentAmount    = contract.PaymentAmount,
                    paymentCurrency  = contract.PaymentCurrency,
                    paymentUnit      = contract.PaymentUnit,
                    paymentType      = contract.PaymentType,
                    riskFlags        = ParseJsonList(contract.RiskFlags),
                    peopleMentioned  = ParseJsonList(contract.PeopleMentioned),
                });
                edges.Add(new
                {
                    id     = $"e-{customerId}-{contractNodeId}",
                    source = customerId,
                    target = contractNodeId,
                });
            }

            foreach (var proj in custProjects)
            {
                var projectNodeId = $"project:{proj.RowKey}";
                nodes.Add(new
                {
                    id        = projectNodeId,
                    type      = "project",
                    label     = proj.Name,
                    parentId  = customerId,
                    projectId = proj.RowKey,
                    status    = proj.Status,
                    startDate = proj.StartDate?.ToString("yyyy-MM-dd"),
                    endDate   = proj.EndDate?.ToString("yyyy-MM-dd"),
                });
                edges.Add(new
                {
                    id     = $"e-{customerId}-{projectNodeId}",
                    source = customerId,
                    target = projectNodeId,
                });
            }
        }

        // Unlinked contracts → synthetic node
        if (unlinked.Count > 0)
        {
            nodes.Add(new
            {
                id            = "__unlinked__",
                type          = "customer",
                label         = "Unlinked",
                contractCount = unlinked.Count,
                projectCount  = 0,
                orgNumber     = "",
                status        = "active",
            });
            edges.Add(new { id = "e-root-unlinked", source = "__root__", target = "__unlinked__" });

            foreach (var contract in unlinked)
            {
                var contractNodeId = $"contract:{contract.RowKey}:unlinked";
                nodes.Add(new
                {
                    id               = contractNodeId,
                    type             = "contract",
                    label            = ContractLabel(contract),
                    parentId         = "__unlinked__",
                    contractId       = contract.RowKey,
                    documentType     = contract.DocumentType,
                    expiryDate       = contract.ExpiryDate?.ToString("yyyy-MM-dd"),
                    reviewState      = contract.ReviewState,
                    fileName         = contract.FileName,
                    status           = contract.Status,
                    counterparty     = contract.PrimaryCounterparty,
                    noticePeriodDays = contract.NoticePeriodDays,
                    noticeDeadline   = contract.NoticeDeadline?.ToString("yyyy-MM-dd"),
                    autoRenewal      = contract.AutoRenewal,
                    paymentAmount    = contract.PaymentAmount,
                    paymentCurrency  = contract.PaymentCurrency,
                    paymentUnit      = contract.PaymentUnit,
                    paymentType      = contract.PaymentType,
                    riskFlags        = ParseJsonList(contract.RiskFlags),
                    peopleMentioned  = ParseJsonList(contract.PeopleMentioned),
                });
                edges.Add(new
                {
                    id     = $"e-unlinked-{contractNodeId}",
                    source = "__unlinked__",
                    target = contractNodeId,
                });
            }
        }

        var payload = new { nodes, edges };
        var res = req.CreateResponse();
        await res.WriteAsJsonAsync(payload);
        res.StatusCode = HttpStatusCode.OK;
        return res;
    }

    private static string ContractLabel(ContractEntity entity)
    {
        if (!string.IsNullOrWhiteSpace(entity.DocumentType)) return entity.DocumentType;
        if (!string.IsNullOrWhiteSpace(entity.FileName)) return entity.FileName;
        return "Contract";
    }

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
