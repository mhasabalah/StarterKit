namespace Tenancy.Domains.Tenants.Exceptions;

/// <summary>Invalid tenant data or lifecycle transition (400).</summary>
public class InvalidTenantException(string message) : DomainException(message);

/// <summary>Invalid branch data or tree operation (400).</summary>
public class InvalidBranchException(string message) : DomainException(message);

/// <summary>Invalid membership/role/invitation operation (400).</summary>
public class InvalidMembershipException(string message) : DomainException(message);

public class TenantIdentifierAlreadyExistsException(string identifier)
    : ConflictException($"The tenant identifier '{identifier}' is already taken.");

public class BranchCodeAlreadyExistsException(string code)
    : ConflictException($"A branch with code '{code}' already exists in this tenant.");

/// <summary>A plan/limit check failed (e.g. MaxUsers) - 403 so clients can show an upgrade prompt.</summary>
public class TenantLimitExceededException(string message) : ForbiddenException(message);
