using Identity.Domains.Onboarding.Models;

namespace Identity.Data.Repository.Onboarding;

public class OnboardingProcessRepository(ApplicationIdentityDbContext dbContext) : IOnboardingProcessRepository
{
    public async Task<OnboardingProcess?> GetActiveByUserIdAsync(Guid userId, CancellationToken ct = default)
        => await dbContext.OnboardingProcesses
            .FirstOrDefaultAsync(
                p => p.UserId == userId
                     && (p.Status == OnboardingStatus.Registered
                         || p.Status == OnboardingStatus.OtpSent
                          || p.Status == OnboardingStatus.PhoneVerified),
                ct);

    public async Task<OnboardingProcess?> GetLatestByUserIdAsync(Guid userId, CancellationToken ct = default)
        => await dbContext.OnboardingProcesses
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<OnboardingProcess?> GetByUserIdAndStatusAsync(
        Guid userId,
        OnboardingStatus status,
        CancellationToken ct = default)
        => await dbContext.OnboardingProcesses
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == status, ct);

    public async Task AddAsync(OnboardingProcess process, CancellationToken ct = default)
        => await dbContext.OnboardingProcesses.AddAsync(process, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await dbContext.SaveChangesAsync(ct);
}
