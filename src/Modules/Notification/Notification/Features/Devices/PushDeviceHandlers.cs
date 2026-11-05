using System.Security.Claims;
using Notification.Contracts.Devices;
using Notification.Features.Inbox;

namespace Notification.Features.Devices;

public sealed class PushDeviceNotFoundException(Guid id) : NotFoundException("PushDevice", id);

internal static class SessionClaim
{
    /// <summary>The caller's session id (<c>sid</c>; JwtBearer maps it to <see cref="ClaimTypes.Sid"/>).</summary>
    public static Guid? Current(IHttpContextService http) =>
        Guid.TryParse(http.GetClaimValue("sid") ?? http.GetClaimValue(ClaimTypes.Sid), out var sid) ? sid : null;
}

// ----- register -------------------------------------------------------------------------------------------------

public class RegisterPushDeviceCommandValidator : AbstractValidator<RegisterPushDeviceCommand>
{
    public RegisterPushDeviceCommandValidator(IStringLocalizer<NotificationResource> localizer)
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage(_ => localizer["Validation.Token.Required"])
            .MaximumLength(RegisterPushDeviceCommand.TokenMaxLength).WithMessage(_ => localizer["Validation.Token.MaxLength"])
            .Must(t => t is null || t.All(c => !char.IsWhiteSpace(c) && !char.IsControl(c)))
            .WithMessage(_ => localizer["Validation.Token.Invalid"]);
        RuleFor(x => x.Platform).IsInEnum().WithMessage(_ => localizer["Validation.Platform.Invalid"]);
        RuleFor(x => x.AppVersion).MaximumLength(RegisterPushDeviceCommand.AppVersionMaxLength)
            .WithMessage(_ => localizer["Validation.AppVersion.MaxLength"]);
        RuleFor(x => x.Locale).MaximumLength(RegisterPushDeviceCommand.LocaleMaxLength)
            .WithMessage(_ => localizer["Validation.Locale.MaxLength"]);
    }
}

/// <summary>
/// Upsert by token. The same token re-registered by another account MOVES to the caller (one install = one user), and
/// re-registering refreshes the session binding, so a token is always bound to the latest sign-in on that install.
/// Keeps at most <see cref="PushOptions.MaxDevicesPerUser"/> devices: the least recently seen are evicted.
/// A concurrent first registration of the same token loses the unique index race and is answered 409 (the client
/// retries and gets the upsert path).
/// </summary>
public class RegisterPushDeviceHandler(
    IPushDeviceRepository devices,
    IHttpContextService http,
    IOptions<PushOptions> options,
    TimeProvider timeProvider,
    ILogger<RegisterPushDeviceHandler> logger) : ICommandHandler<RegisterPushDeviceCommand, RegisterPushDeviceResult>
{
    public async Task<RegisterPushDeviceResult> Handle(RegisterPushDeviceCommand command, CancellationToken cancellationToken)
    {
        var userId = Caller.UserId(http);
        var sessionId = SessionClaim.Current(http);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var token = command.Token.Trim();

        var device = await devices.GetByTokenAsync(token, cancellationToken);
        var created = device is null;
        if (device is null)
        {
            device = new PushDevice { Id = Guid.CreateVersion7(), Token = token, CreatedAt = now };
            devices.Add(device);
        }
        else if (device.UserId != userId)
        {
            logger.LogInformation("Push device {DeviceId} moved from user {OldUser} to {NewUser}", device.Id, device.UserId, userId);
            device.CreatedAt = now;
        }

        device.UserId = userId;
        device.Platform = command.Platform;
        device.SessionId = sessionId;
        device.AppVersion = string.IsNullOrWhiteSpace(command.AppVersion) ? null : command.AppVersion.Trim();
        device.Locale = string.IsNullOrWhiteSpace(command.Locale) ? null : command.Locale.Trim();
        device.LastSeenAt = now;

        await EvictOverflowAsync(userId, device.Id, cancellationToken);
        try
        {
            await devices.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("This device is being registered concurrently; retry the request.");
        }

        return new RegisterPushDeviceResult(device.Id, created);
    }

    private async Task EvictOverflowAsync(Guid userId, Guid keepId, CancellationToken ct)
    {
        var max = Math.Max(1, options.Value.MaxDevicesPerUser);
        var existing = await devices.ListForUserAsync(userId, ct: ct);
        var others = existing.Where(d => d.Id != keepId).ToList(); // most recently seen first
        foreach (var stale in others.Skip(max - 1))
            devices.Remove(stale);
    }
}

// ----- unregister -----------------------------------------------------------------------------------------------

public class UnregisterPushDeviceCommandValidator : AbstractValidator<UnregisterPushDeviceCommand>
{
    public UnregisterPushDeviceCommandValidator(IStringLocalizer<NotificationResource> localizer) =>
        RuleFor(x => x.DeviceId).NotEmpty().WithMessage(_ => localizer["Validation.Id.Required"]);
}

public class UnregisterPushDeviceHandler(IPushDeviceRepository devices, IHttpContextService http)
    : ICommandHandler<UnregisterPushDeviceCommand, UnregisterPushDeviceResult>
{
    public async Task<UnregisterPushDeviceResult> Handle(UnregisterPushDeviceCommand command, CancellationToken cancellationToken)
    {
        var device = await devices.GetForUserAsync(command.DeviceId, Caller.UserId(http), cancellationToken)
                     ?? throw new PushDeviceNotFoundException(command.DeviceId);

        devices.Remove(device);
        await devices.SaveChangesAsync(cancellationToken);
        return new UnregisterPushDeviceResult(device.Id);
    }
}

// ----- list -----------------------------------------------------------------------------------------------------

public class GetMyPushDevicesHandler(NotificationDbContext db, IHttpContextService http)
    : IQueryHandler<GetMyPushDevicesQuery, List<PushDeviceDto>>
{
    public async Task<List<PushDeviceDto>> Handle(GetMyPushDevicesQuery query, CancellationToken cancellationToken)
    {
        var userId = Caller.UserId(http);
        var currentSession = SessionClaim.Current(http);

        var rows = await db.PushDevices.AsNoTracking()
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.LastSeenAt).ThenBy(d => d.Id)
            .Select(d => new { d.Id, d.Platform, d.Token, d.AppVersion, d.Locale, d.SessionId, d.CreatedAt, d.LastSeenAt })
            .ToListAsync(cancellationToken);

        return rows.Select(d => new PushDeviceDto(d.Id, d.Platform, PushDevice.Hint(d.Token), d.AppVersion, d.Locale,
            currentSession is not null && d.SessionId == currentSession, d.CreatedAt, d.LastSeenAt)).ToList();
    }
}
