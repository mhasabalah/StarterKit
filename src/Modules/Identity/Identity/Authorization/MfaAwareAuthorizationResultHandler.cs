using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Shared.Core.Authorization;

namespace Identity.Authorization;

/// <summary>
/// Turns a 403 caused ONLY by <see cref="MfaRequirement"/> into an explicit, machine-readable response:
/// status 403, header <c>X-Auth-Error: mfa_required</c> and an ApiResponse whose first error is <c>mfa_required</c>.
/// Every other result goes to the default handler (401 challenge / plain 403 through the status-code pages).
/// </summary>
public sealed class MfaAwareAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    public const string MfaRequiredMessage =
        "Two-factor authentication is required for your role. Enable it (POST /api/auth/2fa/setup, " +
        "then POST /api/auth/2fa/enable) and sign in again with your authenticator code.";

    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden && IsMfaOnlyFailure(authorizeResult.AuthorizationFailure))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.Headers[AuthorizationErrorCodes.HeaderName] = AuthorizationErrorCodes.MfaRequired;
            await context.Response.WriteAsJsonAsync(new ApiResponse<object>(
                false,
                StatusCodes.Status403Forbidden,
                errors: [AuthorizationErrorCodes.MfaRequired, MfaRequiredMessage]));
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }

    /// <summary>True when the only unmet requirement is the MFA one (the caller HAS the permission/role).</summary>
    public static bool IsMfaOnlyFailure(AuthorizationFailure? failure) =>
        failure is { FailCalled: false } &&
        failure.FailedRequirements.Any() &&
        failure.FailedRequirements.All(r => r is MfaRequirement);
}
