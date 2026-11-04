using Identity.Domains.Users.Models;

namespace Identity.Data.Configurations;

public class AccountDeletionConfiguration : IEntityTypeConfiguration<AccountDeletion>
{
    public void Configure(EntityTypeBuilder<AccountDeletion> builder)
    {
        builder.ToTable("account_deletions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.DeletedAt).IsRequired();
    }
}
