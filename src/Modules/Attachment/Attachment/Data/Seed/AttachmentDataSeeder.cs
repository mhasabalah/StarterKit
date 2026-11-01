namespace Attachment.Data.Seed;

public class AttachmentDataSeeder : IDataSeeder
{
    public Type DbContextType => typeof(AttachmentDbContext);

    public async Task SeedAllAsync()
    {
        // No initial seed data needed for attachments
        await Task.CompletedTask;
    }
}
