using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;

namespace HqAgent.Api;

/// <summary>
/// Tests role-based authorization for the signed-in user.
/// GET /api/auth-test?action=admin  — requires the "admin" role
/// GET /api/auth-test?action=user   — requires the "user" or "admin" role
/// App access is enforced by RequireAccessMiddleware before this runs.
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

        var requiredRole = action switch
        {
            "admin" => Roles.Admin,
            "user"  => Roles.User,
            _       => null
        };

        if (requiredRole is null)
        {
            var bad = req.CreateResponse();
            await bad.WriteStringAsync("Invalid action");
            bad.StatusCode = HttpStatusCode.BadRequest;
            return bad;
        }

        RoleGuard guard;
        try
        {
            guard = await RoleGuard.CheckAsync(req, _httpFactory, _appId, requiredRole);
        }
        catch (Exception)
        {
            var err = req.CreateResponse();
            await err.WriteStringAsync("Auth service unavailable");
            err.StatusCode = HttpStatusCode.ServiceUnavailable;
            return err;
        }

        var res = req.CreateResponse();
        await res.WriteAsJsonAsync(new { allowed = guard.Allowed, roles = guard.RoleIds.ToArray() });
        res.StatusCode = guard.Allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
        return res;
    }
}
