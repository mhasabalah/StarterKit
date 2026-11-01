using System.Security.Cryptography;
using System.Text;
using System.Net;
using Hangfire.Dashboard;
using Microsoft.Extensions.Options;
using Shared.Core.Authorization;

namespace Api.Infrastructure;

public sealed class HangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    private const string BasicAuthScheme = "Basic";

    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        if (HasAdminAccess(httpContext))
        {
            return true;
        }

        var options = httpContext.RequestServices
            .GetRequiredService<IOptions<HangfireDashboardAuthOptions>>()
            .Value;

        if (!options.Enabled)
        {
            return true;
        }

        if (options.AllowLocalRequestsWithoutAuthentication && IsLocalRequest(httpContext))
        {
            return true;
        }

        if (TryValidateBasicCredentials(httpContext, options))
        {
            return true;
        }

        ChallengeBasicAuth(httpContext);
        return false;
    }

    private static bool HasAdminAccess(HttpContext httpContext)
    {
        var user = httpContext.User;

        return user.Identity?.IsAuthenticated == true &&
               (user.IsInRole(RoleConstants.Admin) || user.IsInRole(RoleConstants.SuperAdmin));
    }

    private static bool IsLocalRequest(HttpContext httpContext)
    {
        var connection = httpContext.Connection;

        if (connection.RemoteIpAddress is null)
        {
            return false;
        }

        if (connection.LocalIpAddress is not null)
        {
            return connection.RemoteIpAddress.Equals(connection.LocalIpAddress);
        }

        return IPAddress.IsLoopback(connection.RemoteIpAddress);
    }

    private static bool TryValidateBasicCredentials(
        HttpContext httpContext,
        HangfireDashboardAuthOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Username) || string.IsNullOrWhiteSpace(options.Password))
        {
            return false;
        }

        if (!httpContext.Request.Headers.TryGetValue("Authorization", out var headerValue))
        {
            return false;
        }

        var authorizationHeader = headerValue.ToString();
        if (!authorizationHeader.StartsWith($"{BasicAuthScheme} ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var encodedCredentials = authorizationHeader[(BasicAuthScheme.Length + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(encodedCredentials))
        {
            return false;
        }

        string decodedCredentials;
        try
        {
            decodedCredentials = Encoding.UTF8.GetString(Convert.FromBase64String(encodedCredentials));
        }
        catch (FormatException)
        {
            return false;
        }

        var separatorIndex = decodedCredentials.IndexOf(':');
        if (separatorIndex <= 0)
        {
            return false;
        }

        var providedUserName = decodedCredentials[..separatorIndex];
        var providedPassword = decodedCredentials[(separatorIndex + 1)..];

        return SecureEquals(providedUserName, options.Username) &&
               SecureEquals(providedPassword, options.Password);
    }

    private static bool SecureEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);

        return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static void ChallengeBasicAuth(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
        httpContext.Response.Headers.WWWAuthenticate = "Basic realm=\"Hangfire Dashboard\"";
    }
}
