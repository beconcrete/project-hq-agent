using System.Net;
using System.Net.Http.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HqAgent.Functions.Middleware;

/// <summary>
/// Validates every incoming HTTP request against usermgmt before the function runs.
/// Non-HTTP triggers (blob, queue) and functions in <see cref="PublicFunctions"/> pass through.
/// </summary>
public class RequireAccessMiddleware : IFunctionsWorkerMiddleware
{
    /// <summary>Functions that do not require an authenticated user.</summary>
    private static readonly HashSet<string> PublicFunctions =
        new(StringComparer.OrdinalIgnoreCase) { nameof(GetConfig) };

    private readonly IHttpClientFactory _httpFactory;
    private readonly string _appId;
    private readonly ILogger<RequireAccessMiddleware> _logger;

    public RequireAccessMiddleware(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ILogger<RequireAccessMiddleware> logger)
    {
        _httpFactory = httpFactory;
        _appId = config["APP_ID"] ?? "hqagents";
        _logger = logger;
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var req = await context.GetHttpRequestDataAsync();

        // Non-HTTP triggers and public endpoints skip auth
        if (req is null || PublicFunctions.Contains(context.FunctionDefinition.Name))
        {
            await next(context);
            return;
        }

        var token = req.Headers.TryGetValues("x-auth-token", out var vals)
            ? vals.FirstOrDefault()?.Replace("Bearer ", "").Trim()
            : null;

        if (string.IsNullOrEmpty(token))
        {
            await WriteResponseAsync(context, req, HttpStatusCode.Unauthorized, "Unauthorized");
            return;
        }

        UserMeResponse? me;
        try
        {
            var client = _httpFactory.CreateClient();
            using var hmReq = new HttpRequestMessage(
                HttpMethod.Get, "https://usermanagement.beconcrete.se/api/v1/me");
            hmReq.Headers.Add("X-Auth-Token", $"Bearer {token}");

            using var hmRes = await client.SendAsync(hmReq);

            if (!hmRes.IsSuccessStatusCode)
            {
                await WriteResponseAsync(context, req, hmRes.StatusCode, "Forbidden");
                return;
            }

            me = await hmRes.Content.ReadFromJsonAsync<UserMeResponse>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error contacting usermgmt");
            await WriteResponseAsync(context, req, HttpStatusCode.ServiceUnavailable, "Auth service unavailable");
            return;
        }

        if (me is null || !me.Apps.Contains(_appId, StringComparer.OrdinalIgnoreCase))
        {
            await WriteResponseAsync(context, req, HttpStatusCode.Forbidden, "No access to this application");
            return;
        }

        // Make userId available to downstream function handlers
        context.Items["userId"] = me.UserId;
        await next(context);
    }

    private static async Task WriteResponseAsync(
        FunctionContext ctx, HttpRequestData req, HttpStatusCode status, string body)
    {
        var res = req.CreateResponse(status);
        await res.WriteStringAsync(body);
        ctx.GetInvocationResult().Value = res;
    }

    private record UserMeResponse(string UserId, string Status, List<string> Apps);
}
