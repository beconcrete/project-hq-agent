using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;

namespace HqAgent.Functions;

/// <summary>
/// Public endpoint — returns Auth0 config needed by the frontend SPA.
/// No auth required; Auth0 domain and client ID are not secrets.
/// </summary>
public class GetConfig
{
    private readonly IConfiguration _config;

    public GetConfig(IConfiguration config)
    {
        _config = config;
    }

    [Function(nameof(GetConfig))]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config")]
        HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");

        var payload = new
        {
            auth0Domain = _config["AUTH0_DOMAIN"] ?? "",
            auth0ClientId = _config["AUTH0_CLIENT_ID"] ?? "",
        };

        await response.WriteStringAsync(JsonSerializer.Serialize(payload));
        return response;
    }
}
