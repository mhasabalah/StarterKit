namespace Identity.Options;

public sealed class OnboardingOptions
{
    public int TimeoutMinutes { get; set; } = 60;
    public int MaxResends { get; set; } = 3;
}
