using Shared.Contracts.Attributes;

namespace Attachment.Contracts.Attachments.Dtos;

public enum AttachmentType
{
    [LocalizedDescription("Image", "صورة")]
    Image = 1,

    [LocalizedDescription("Document", "مستند")]
    Document = 2,

    [LocalizedDescription("Video", "فيديو")]
    Video = 3,

    [LocalizedDescription("Audio", "صوت")]
    Audio = 4,

    [LocalizedDescription("Other", "آخر")]
    Other = 5
}

public enum AttachmentCategory
{
    [LocalizedDescription("General", "عام")]
    General = 1,

    [LocalizedDescription("User Avatar", "صورة شخصية")]
    UserAvatar = 2,

    [LocalizedDescription("Product Image", "صورة منتج")]
    ProductImage = 3,

    [LocalizedDescription("Product Document", "مستند منتج")]
    ProductDocument = 4,

    [LocalizedDescription("Company Logo", "شعار شركة")]
    CompanyLogo = 5
}

/// <summary>
/// The kind of entity an attachment belongs to. Add a member here when a new module
/// needs to own attachments, map it in <c>AttachmentCategoryHelper</c>, and register an
/// <c>IAttachmentEntityAccessPolicy</c> for it from the owning module
/// (<c>services.AddAttachmentEntityPolicy&lt;TPolicy&gt;(EntityType.X)</c>).
/// Without a policy the public Attachment endpoints deny attaching files to that entity type.
/// </summary>
public enum EntityType
{
    [LocalizedDescription("User", "مستخدم")]
    User = 1,

    [LocalizedDescription("Product", "منتج")]
    Product = 2,

    [LocalizedDescription("Company", "شركة")]
    Company = 3,

    [LocalizedDescription("Other", "آخر")]
    Other = 4,

    /// <summary>
    /// Files of a bulk import/export job (DataTransfer module): uploaded sources, export results, error reports.
    /// No HTTP policy is registered: they are read only through the DataTransfer endpoints (trusted queries).
    /// </summary>
    [LocalizedDescription("Data transfer job", "مهمة نقل بيانات")]
    DataTransferJob = 5
}
