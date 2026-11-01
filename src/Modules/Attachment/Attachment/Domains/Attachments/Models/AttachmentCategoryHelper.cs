using Attachment.Contracts.Attachments.Dtos;

namespace Attachment.Domains.Attachments.Models;

/// <summary>
/// Helper class to calculate AttachmentCategory from EntityType and AttachmentType.
/// This ensures Category is always derived data and prevents sync issues.
/// </summary>
public static class AttachmentCategoryHelper
{
    /// <summary>
    /// Determines the attachment category based on entity type and attachment type.
    /// Category is derived data - it should never be set manually.
    /// </summary>
    public static AttachmentCategory DetermineCategory(EntityType? entityType, AttachmentType attachmentType)
    {
        // No entity type = general category
        if (entityType == null)
        {
            return AttachmentCategory.General;
        }

        // Determine category based on entity type and attachment type
        return (entityType.Value, attachmentType) switch
        {
            // User attachments
            (EntityType.User, AttachmentType.Image) => AttachmentCategory.UserAvatar,
            (EntityType.User, _) => AttachmentCategory.General,

            // Product attachments
            (EntityType.Product, AttachmentType.Image) => AttachmentCategory.ProductImage,
            (EntityType.Product, _) => AttachmentCategory.ProductDocument,

            // Company attachments
            (EntityType.Company, AttachmentType.Image) => AttachmentCategory.CompanyLogo,
            (EntityType.Company, _) => AttachmentCategory.General,

            // Default fallback
            _ => AttachmentCategory.General
        };
    }
}
