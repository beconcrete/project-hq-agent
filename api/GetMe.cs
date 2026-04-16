using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;

namespace HqAgent.Api;

/// <summary>
/// Returns the authenticated user's profile. Auth is enforced by RequireAccessMiddleware.
/// The frontend calls this instead of hitting usermanagement.beconcrete.se directly,
/// avoiding cross-origin requests from the browser.
/// </summary>
public class GetMe
{
    private readonly string _appId;

    public GetMe(IConfiguration config)
    {
        _appId = config["APP_ID"] ?? "hqagents";
    }

    [Function("GetMe")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "me")] HttpRequestData req,
        FunctionContext context)
    {
        var userId = context.Items.TryGetValue("userId", out var uid) ? uid?.ToString() : null;

        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteAsJsonAsync(new
        {
            userId,
            apps = new[] { _appId }
        });
        return res;
    }
}
