namespace Shared.Core.Data.Seed;

public interface IDataSeeder
{
    Task SeedAllAsync();
    Type DbContextType { get; }
}
