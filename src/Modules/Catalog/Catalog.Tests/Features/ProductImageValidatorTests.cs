using Attachment.Contracts.Attachments.Dtos;
using Attachment.Contracts.Attachments.Queries;
using MediatR;
using Shared.Core.Exceptions;

namespace Catalog.Tests.Features;

public class ProductImageValidatorTests
{
    private readonly Mock<ISender> _sender = new();
    private readonly ProductImageValidator _validator;

    public ProductImageValidatorTests()
    {
        _validator = new ProductImageValidator(_sender.Object);
    }

    private void SetupAttachment(Guid id, AttachmentType type) =>
        _sender.Setup(s => s.Send(It.Is<GetAttachmentByIdQuery>(q => q.Id == id), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetAttachmentByIdResult(new AttachmentDto { Id = id, Type = type }));

    [Fact]
    public async Task ImageAttachment_Passes()
    {
        var id = Guid.NewGuid();
        SetupAttachment(id, AttachmentType.Image);

        var act = () => _validator.EnsureValidImageAsync(id);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NonImageAttachment_ThrowsInvalidProduct()
    {
        var id = Guid.NewGuid();
        SetupAttachment(id, AttachmentType.Document);

        var act = () => _validator.EnsureValidImageAsync(id);

        await act.Should().ThrowAsync<InvalidProductException>().WithMessage("*not an image*");
    }

    [Fact]
    public async Task MissingAttachment_IsTranslatedToInvalidProduct()
    {
        var id = Guid.NewGuid();
        _sender.Setup(s => s.Send(It.IsAny<GetAttachmentByIdQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("Attachment", id));

        var act = () => _validator.EnsureValidImageAsync(id);

        await act.Should().ThrowAsync<InvalidProductException>().WithMessage("*not found*");
    }

    [Fact]
    public async Task InaccessibleAttachment_IsTranslatedToInvalidProduct()
    {
        _sender.Setup(s => s.Send(It.IsAny<GetAttachmentByIdQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException());

        var act = () => _validator.EnsureValidImageAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidProductException>().WithMessage("*not accessible*");
    }
}
