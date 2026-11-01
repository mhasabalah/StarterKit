namespace Shared.Email.Models;

/// <summary>Sent to admin when a subscriber uploads payment proof for renewal review.</summary>
public sealed class AdminPaymentProofReceivedModel
{
    public string SubscriberName { get; set; } = string.Empty;
    public string ApplicantEmail { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string ProofUploadedAt { get; set; } = string.Empty;
    public string AdminReviewUrl { get; set; } = string.Empty;

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["SubscriberName"] = SubscriberName,
        ["ApplicantEmail"] = ApplicantEmail,
        ["PlanName"] = PlanName,
        ["ProofUploadedAt"] = ProofUploadedAt,
        ["AdminReviewUrl"] = AdminReviewUrl
    };

    public sealed class Metadata : IEmailTemplate
    {
        public EmailTemplate Template => EmailTemplate.AdminPaymentProofReceived;

        public string GetSubject(EmailCulture culture) => culture == EmailCulture.Ar
            ? "إثبات دفع جديد بانتظار المراجعة"
            : "New Payment Proof Awaiting Review";
    }
}
