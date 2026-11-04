# add-aggregate templates

These are trimmed from `Catalog/Domains/Products` and `Catalog/Domains/Tags`.

## Model

```csharp
using Shared.Core.MultiTenancy;

namespace {{Module}}.Domains.{{Aggregate}}s.Models;

public class {{Aggregate}} : Aggregate<Guid>, IMustHaveTenant          // + IBranchScoped if branch-owned
{
    public Guid TenantId { get; set; }                                   // stamped by TenantEntityInterceptor

    public const int NameMaxLength = 200;
    public const int CodeMaxLength = 50;
    public const int DescriptionMaxLength = 2000;

    private readonly List<{{Aggregate}}Line> _lines = [];

    public LocalizedText Name { get; private set; } = default!;
    public string Code { get; private set; } = default!;
    public string? Description { get; private set; }
    public {{Aggregate}}Status Status { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public IReadOnlyCollection<{{Aggregate}}Line> Lines => _lines.AsReadOnly();

    private {{Aggregate}}() { }                                          // EF Core

    public static {{Aggregate}} Create(LocalizedText name, string code, string? description = null)
    {
        var entity = new {{Aggregate}} { Id = Guid.NewGuid(), Status = {{Aggregate}}Status.Draft };
        entity.ApplyDetails(name, code, description);
        entity.AddDomainEvent(new {{Aggregate}}CreatedEvent(entity.Id));
        return entity;
    }

    public void Update(LocalizedText name, string code, string? description)
    {
        ApplyDetails(name, code, description);
        Touch();
        AddDomainEvent(new {{Aggregate}}UpdatedEvent(Id));
    }

    public void Activate()
    {
        if (Status == {{Aggregate}}Status.Active) return;                 // idempotent
        if (Status == {{Aggregate}}Status.Archived)
            throw new Invalid{{Aggregate}}Exception("An archived {{aggregate}} cannot be activated.");
        Status = {{Aggregate}}Status.Active;
        Touch();
        // Cross-module fact? also: AddIntegrationEvent(new {{Aggregate}}ActivatedIntegrationEvent { ... });
    }

    private void ApplyDetails(LocalizedText name, string code, string? description)
    {
        if (name is null || string.IsNullOrWhiteSpace(name.En) || string.IsNullOrWhiteSpace(name.Ar))
            throw new Invalid{{Aggregate}}Exception("Name is required in English and Arabic.");
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > CodeMaxLength)
            throw new Invalid{{Aggregate}}Exception($"Code is required (max {CodeMaxLength}).");
        Name = LocalizedText.Create(name.En.Trim(), name.Ar.Trim());
        Code = code.Trim().ToUpperInvariant();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (Description?.Length > DescriptionMaxLength)
            throw new Invalid{{Aggregate}}Exception($"Description must not exceed {DescriptionMaxLength} characters.");
    }

    public {{Aggregate}}Line AddLine(string description, int quantity)
    {
        if (quantity <= 0) throw new Invalid{{Aggregate}}Exception("Quantity must be positive.");
        var line = {{Aggregate}}Line.Create(Id, description, quantity);
        _lines.Add(line);
        Touch();
        return line;
    }

    private void Touch() => UpdatedAt = DateTime.UtcNow;                // Version is bumped centrally - never here
}

/// <summary>Child entity: changed only through its root. Tenant-data contexts: children are IMustHaveTenant too.</summary>
public class {{Aggregate}}Line : Entity<Guid>, IMustHaveTenant
{
    public const int DescriptionMaxLength = 200;

    public Guid TenantId { get; set; }
    public Guid {{Aggregate}}Id { get; private set; }
    public string Description { get; private set; } = default!;
    public int Quantity { get; private set; }

    private {{Aggregate}}Line() { }                                      // EF Core

    internal static {{Aggregate}}Line Create(Guid {{aggregate}}Id, string description, int quantity) =>
        new() { Id = Guid.NewGuid(), {{Aggregate}}Id = {{aggregate}}Id, Description = description.Trim(), Quantity = quantity };
}
```

No children? Delete `_lines`, `Lines`, `AddLine` and the `{{Aggregate}}Line` class, plus the `HasMany` and
`Navigation` lines in the configuration. In tenant-data contexts a child also needs its own RLS call in the
migration. A child also needs its own configuration, and its **key must be `ValueGeneratedNever()`**. The id is
assigned in the domain, so otherwise EF treats a child added through `Lines` as an existing row and the first save
fails with a bogus 409. `ClientGeneratedKeyTests` enforces this:

```csharp
public class {{Aggregate}}LineConfiguration : IEntityTypeConfiguration<{{Aggregate}}Line>
{
    public void Configure(EntityTypeBuilder<{{Aggregate}}Line> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Description).IsRequired().HasMaxLength({{Aggregate}}Line.DescriptionMaxLength);
    }
}
```

The enum lives in `{{Module}}.Contracts/{{Aggregate}}s/Enums/` when it is exposed through the API. Give it
`[LocalizedDescription]` values so the enum lookup endpoint can serve labels:

```csharp
using System.Text.Json.Serialization;

namespace {{Module}}.Contracts.{{Aggregate}}s.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum {{Aggregate}}Status
{
    [LocalizedDescription("Draft", "مسودة")] Draft = 1,
    [LocalizedDescription("Active", "نشط")] Active = 2,
    [LocalizedDescription("Archived", "مؤرشف")] Archived = 3
}
```

Add `global using {{Module}}.Contracts.{{Aggregate}}s.Enums;` to both GlobalUsings files.

## Events and exceptions

```csharp
namespace {{Module}}.Domains.{{Aggregate}}s.Events;

public record {{Aggregate}}CreatedEvent(Guid {{Aggregate}}Id) : IDomainEvent;
public record {{Aggregate}}UpdatedEvent(Guid {{Aggregate}}Id) : IDomainEvent;
```

```csharp
namespace {{Module}}.Domains.{{Aggregate}}s.Exceptions;

public class {{Aggregate}}NotFoundException(Guid id) : NotFoundException("{{Aggregate}}", id);
public class Invalid{{Aggregate}}Exception(string message) : BadRequestException(message);
public class {{Aggregate}}CodeAlreadyExistsException(string code)
    : ConflictException($"A {{aggregate}} with code '{code}' already exists.");
```

## EF configuration

```csharp
namespace {{Module}}.Data.Configurations;

public class {{Aggregate}}Configuration : IEntityTypeConfiguration<{{Aggregate}}>
{
    public const string CodeIndexName = "ux_{{aggregate}}s_code";

    public void Configure(EntityTypeBuilder<{{Aggregate}}> builder)
    {
        builder.HasKey(x => x.Id);

        builder.OwnsOne(x => x.Name, name =>
        {
            name.Property(n => n.En).HasColumnName("name_en").IsRequired().HasMaxLength({{Aggregate}}.NameMaxLength);
            name.Property(n => n.Ar).HasColumnName("name_ar").IsRequired().HasMaxLength({{Aggregate}}.NameMaxLength);
        });
        builder.Navigation(x => x.Name).IsRequired();

        builder.Property(x => x.Code).IsRequired().HasMaxLength({{Aggregate}}.CodeMaxLength);
        builder.Property(x => x.Description).HasMaxLength({{Aggregate}}.DescriptionMaxLength);
        builder.Property(x => x.Status).IsRequired().HasConversion<string>().HasMaxLength(30);

        builder.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.{{Aggregate}}Id).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasDatabaseName(CodeIndexName); // tenant-first
    }
}
```

## Repository

```csharp
using {{Module}}.Data.Configurations;

namespace {{Module}}.Data.Repository;

public interface I{{Aggregate}}Repository
{
    Task<{{Aggregate}}?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync({{Aggregate}} entity, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public class {{Aggregate}}Repository({{Module}}DbContext context) : I{{Aggregate}}Repository
{
    public Task<{{Aggregate}}?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.{{Aggregate}}s.Include(x => x.Lines).AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task AddAsync({{Aggregate}} entity, CancellationToken cancellationToken = default)
    {
        await context.{{Aggregate}}s.AddAsync(entity, cancellationToken);
        await SaveChangesAsync(cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation({{Aggregate}}Configuration.CodeIndexName))
        {
            var code = context.ChangeTracker.Entries<{{Aggregate}}>().Select(e => e.Entity.Code).FirstOrDefault() ?? "?";
            throw new {{Aggregate}}CodeAlreadyExistsException(code);
        }
    }
}
```

The unique index is the last line of defence against races. Also check it up front in the handler
(`AnyAsync(x => x.Code == code)`), so users get a friendly 409.

## Domain event handler (same module)

```csharp
namespace {{Module}}.Domains.{{Aggregate}}s.EventHandlers;

public class {{Aggregate}}CreatedEventHandler(ILogger<{{Aggregate}}CreatedEventHandler> logger)
    : INotificationHandler<{{Aggregate}}CreatedEvent>
{
    public Task Handle({{Aggregate}}CreatedEvent notification, CancellationToken cancellationToken)
    {
        logger.LogInformation("{{Aggregate}} {Id} created", notification.{{Aggregate}}Id);
        return Task.CompletedTask;
    }
}
```

Handlers run after the commit, so a failure here does not roll back the save. If the reaction must succeed
together with the command, make it part of the command instead.
