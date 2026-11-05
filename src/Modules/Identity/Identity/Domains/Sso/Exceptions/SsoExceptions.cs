using Identity.Resources;

namespace Identity.Domains.Sso.Exceptions;

public sealed class SsoConnectionNotFoundException(Guid id)
    : NotFoundException(IdentityText.Get("Sso.ConnectionNotFound", id));

public sealed class SsoDomainNotFoundException(string domain)
    : NotFoundException(IdentityText.Get("Sso.DomainNotFound", domain));

/// <summary>An invalid connection change. Built from a resource key (en/ar) and its format arguments.</summary>
public sealed class InvalidSsoConnectionException : BadRequestException
{
    public InvalidSsoConnectionException(string resourceKey, params object?[] args)
        : base(IdentityText.Get(resourceKey, args))
    {
        ErrorCode = "sso_connection_invalid";
        ResourceKey = resourceKey;
    }

    /// <summary>The resource key (stable, for tests and logs; the message is localized).</summary>
    public string ResourceKey { get; }
}

/// <summary>Another active connection (any tenant) already owns this verified domain.</summary>
public sealed class SsoDomainTakenException : ConflictException
{
    public SsoDomainTakenException(string domain)
        : base(IdentityText.Get("Sso.DomainTaken", domain)) => ErrorCode = "sso_domain_taken";
}

public sealed class SsoDomainUnverifiedException : ConflictException
{
    public SsoDomainUnverifiedException(string recordName)
        : base(IdentityText.Get("Sso.DomainUnverified", recordName))
        => ErrorCode = "sso_domain_unverified";
}

/// <summary>Password sign-in (or reset) for a user whose domain enforces SSO.</summary>
public sealed class SsoRequiredException : ForbiddenException
{
    public SsoRequiredException(Guid connectionId)
        : base(IdentityText.Get("Sso.Required"))
    {
        ErrorCode = "sso_required";
        ConnectionId = connectionId;
    }

    public Guid ConnectionId { get; }
}

/// <summary>The IdP response was rejected (signature, audience, replay, state, ...). Details only go to the log.</summary>
public sealed class SsoLoginFailedException(string reason) : UnauthorizedException(IdentityText.Get("Sso.LoginFailed"))
{
    public string Reason { get; } = reason;
}
