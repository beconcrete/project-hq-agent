using System.Net;
using HqAgent.Shared.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;

namespace HqAgent.Api;

/// <summary>
/// Returns timereport entries for a project, grouped by month.
/// GET /api/project-timereports?projectId={id}
/// </summary>
public class ProjectTimereports
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly TimereportStorageService _timereports;
    private readonly string _appId;

    public ProjectTimereports(
        IHttpClientFactory httpFactory,
        TimereportStorageService timereports,
        IConfiguration config)
    {
        _httpFactory = httpFactory;
        _timereports = timereports;
        _appId       = config["APP_ID"] ?? "hqagents";
    }

    [Function("ProjectTimereports")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "project-timereports")] HttpRequestData req,
        FunctionContext context)
    {
        RoleGuard guard;
        try { guard = await RoleGuard.CheckAsync(req, _httpFactory, _appId, Roles.User); }
        catch { return await PlainResponse(req, HttpStatusCode.ServiceUnavailable, "Auth service unavailable"); }

        if (!guard.Allowed)
            return await PlainResponse(req, HttpStatusCode.Forbidden, "Forbidden");

        var projectId = System.Web.HttpUtility.ParseQueryString(req.Url.Query)["projectId"];
        if (string.IsNullOrWhiteSpace(projectId))
            return await PlainResponse(req, HttpStatusCode.BadRequest, "projectId is required");

        var ct      = context.CancellationToken;
        var entries = await _timereports.QueryAsync(projectId: projectId, ct: ct);

        var months = entries
            .GroupBy(e => e.ReportDate[..7])
            .OrderByDescending(g => g.Key)
            .Select(g => new
            {
                month      = g.Key,
                totalHours = g.Sum(e => e.Hours),
                entries    = g
                    .OrderBy(e => e.ReportDate)
                    .ThenBy(e => e.RowKey)
                    .Select(e => new
                    {
                        date     = e.ReportDate,
                        employee = e.PartitionKey,
                        hours    = e.Hours,
                        note     = e.Note,
                    })
                    .ToArray(),
            })
            .ToArray();

        var res = req.CreateResponse();
        await res.WriteAsJsonAsync(new { months });
        res.StatusCode = HttpStatusCode.OK;
        return res;
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
