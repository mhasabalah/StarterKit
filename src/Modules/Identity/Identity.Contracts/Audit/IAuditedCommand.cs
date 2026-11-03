namespace Identity.Contracts.Audit;

/// <summary>
/// Marks an Identity command whose handler records an admin audit entry. The Identity module runs such commands
/// in ONE database transaction and saves the audit rows inside it, so the change and its audit entry commit (or roll
/// back) together.
/// </summary>
public interface IAuditedCommand;
