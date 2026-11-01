using Identity.Domains.Onboarding.Models;

namespace Identity.Data.Repository.Onboarding;

public interface IOnboardingProcessRepository
{
    Task<OnboardingProcess?> GetActiveByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<OnboardingProcess?> GetLatestByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<OnboardingProcess?> GetByUserIdAndStatusAsync(Guid userId, OnboardingStatus status, CancellationToken ct = default);
    Task AddAsync(OnboardingProcess process, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
