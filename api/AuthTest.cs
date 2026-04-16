using System.Net;
using System.Net.Http.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;

namespace HqAgent.Api;

/// <summary>
/// Tests role-based authorization for the signed-in user.
/// GET /api/auth-test?action=admin  — requires the "admin" role
/// GET /api/auth-test?action=user   — requires the "user" or "admin" role
/// Auth (app access) is enforced by RequireAccessMiddleware before this runs.
/// </summary>
public class AuthTest
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly string _appId;

    public AuthTest(IHttpClientFactory httpFactory, IConfiguration config)
    {
        _httpFactory = httpFactory;
        _appId = config["APP_ID"] ?? "hqagents";
    }

    [Function("AuthTest")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "auth-test")] HttpRequestData req,
        FunctionContext context)
    {
        var action = req.Query["action"] ?? "";

        var token = req.Headers.TryGetValues("x-auth-token", out var vals)
            ? vals.FirstOrDefault()?.Replace("Bearer ", "").Trim()
            : null;

        var client = _httpFactory.CreateClient();
        using var rolesReq = new HttpRequestMessage(
            HttpMethod.Get, $"https://id.beconcrete.se/api/v1/roles?appId={_appId}");
        rolesReq.Headers.Add("X-Auth-Token", $"Bearer {token}");

        using var rolesRes = await client.SendAsync(rolesReq);
        var roles = rolesRes.IsSuccessStatusCode
            ? await rolesRes.Content.ReadFromJsonAsync<List<RoleEntry>>() ?? []
            : [];

        var roleIds = roles.Select(r => r.RoleId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        bool allowed = action switch
        {
            "admin" => roleIds.Contains("admin"),
            "user"  => roleIds.Contains("user") || roleIds.Contains("admin"),
            _       => false
        };

        var res = req.CreateResponse();
        await res.WriteAsJsonAsync(new { allowed, roles = roleIds.ToArray() });
        res.StatusCode = allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
        return res;
    }

    private record RoleEntry(string AppId, string RoleId, string RoleName);
}
