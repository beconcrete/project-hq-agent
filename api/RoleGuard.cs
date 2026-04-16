using System.Net.Http.Json;
using Microsoft.Azure.Functions.Worker.Http;

namespace HqAgent.Api;

/// <summary>
/// Checks whether the signed-in user holds at least one of the required roles.
/// Admin is a superset of all roles — a user with the admin role always passes.
///
/// Roles are fetched fresh on every call (no caching) so revocations take effect immediately.
///
/// Network errors propagate as exceptions — callers should catch and return 503.
/// </summary>
public class RoleGuard
{
    public bool Allowed { get; }
    public IReadOnlySet<string> RoleIds { get; }

    private RoleGuard(bool allowed, HashSet<string> roleIds)
    {
        Allowed = allowed;
        RoleIds = roleIds;
    }

    /// <summary>
    /// Fetches the user's roles from Be Concrete ID and checks against <paramref name="requiredRoles"/>.
    /// The user passes if they hold the admin role OR at least one of the required roles.
    /// </summary>
    public static async Task<RoleGuard> CheckAsync(
        HttpRequestData req,
        IHttpClientFactory httpFactory,
        string appId,
        params string[] requiredRoles)
    {
        var token = req.Headers.TryGetValues("x-auth-token", out var vals)
            ? vals.FirstOrDefault()?.Replace("Bearer ", "").Trim()
            : null;

        var client = httpFactory.CreateClient();
        using var rolesReq = new HttpRequestMessage(
            HttpMethod.Get, $"https://id.beconcrete.se/api/v1/roles?appId={appId}");
        rolesReq.Headers.Add("X-Auth-Token", $"Bearer {token}");

        // Network exceptions propagate — caller surfaces as 503
        using var rolesRes = await client.SendAsync(rolesReq);

        var entries = rolesRes.IsSuccessStatusCode
            ? await rolesRes.Content.ReadFromJsonAsync<List<RoleEntry>>() ?? []
            : [];

        var roleIds = entries.Select(r => r.RoleId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Admin is a superset — always passes any role check
        var allowed = roleIds.Contains(Roles.Admin)
            || requiredRoles.Any(r => roleIds.Contains(r));

        return new RoleGuard(allowed, roleIds);
    }

    private record RoleEntry(string AppId, string RoleId, string RoleName);
}
