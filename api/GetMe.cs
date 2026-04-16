using System.Net;
using System.Net.Http.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;

namespace HqAgent.Api;

/// <summary>
/// Returns the authenticated user's profile and roles.
/// The frontend uses roles[] to gate UI — admin-only features are hidden for non-admin users.
/// Auth is enforced by RequireAccessMiddleware.
/// </summary>
public class GetMe
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly string _appId;

    public GetMe(IHttpClientFactory httpFactory, IConfiguration config)
    {
        _httpFactory = httpFactory;
        _appId = config["APP_ID"] ?? "hqagents";
    }

    [Function("GetMe")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "me")] HttpRequestData req,
        FunctionContext context)
    {
        var userId = context.Items.TryGetValue("userId", out var uid) ? uid?.ToString() : null;

        var token = req.Headers.TryGetValues("x-auth-token", out var vals)
            ? vals.FirstOrDefault()?.Replace("Bearer ", "").Trim()
            : null;

        // Fetch roles so the frontend can gate UI without an extra round-trip.
        // A roles fetch failure is non-fatal — the user is still authenticated.
        string[] roles = [];
        try
        {
            var client = _httpFactory.CreateClient();
            using var rolesReq = new HttpRequestMessage(
                HttpMethod.Get, $"https://id.beconcrete.se/api/v1/roles?appId={_appId}");
            rolesReq.Headers.Add("X-Auth-Token", $"Bearer {token}");
            using var rolesRes = await client.SendAsync(rolesReq);
            if (rolesRes.IsSuccessStatusCode)
            {
                var entries = await rolesRes.Content.ReadFromJsonAsync<List<RoleEntry>>() ?? [];
                roles = entries.Select(r => r.RoleId).ToArray();
            }
        }
        catch { /* non-fatal — roles stays empty */ }

        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(new { userId, apps = new[] { _appId }, roles });
        return res;
    }

    private record RoleEntry(string AppId, string RoleId, string RoleName);
}
