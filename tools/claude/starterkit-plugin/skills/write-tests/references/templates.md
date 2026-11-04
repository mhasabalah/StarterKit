# write-tests templates

These are trimmed from `Catalog.Tests`, `Catalog.IntegrationTests` and `Api.E2ETests/Catalog`.

## Domain unit test

```csharp
namespace {{Module}}.Tests.Domain;

public class {{Aggregate}}Tests
{
    [Fact]
    public void Create_RaisesCreatedEvent_AndStartsAsDraft()
    {
        var entity = {{Aggregate}}.Create(LocalizedText.Create("Name", "اسم"), "code-1");

        entity.Status.Should().Be({{Aggregate}}Status.Draft);
        entity.Code.Should().Be("CODE-1");
        entity.DomainEvents.Should().ContainSingle(e => e is {{Aggregate}}CreatedEvent);
    }

    [Fact]
    public void Activate_WhenArchived_Throws()
    {
        var entity = TestSupport.{{Aggregate}}In({{Aggregate}}Status.Archived);

        var act = () => entity.Activate();

        act.Should().Throw<Invalid{{Aggregate}}Exception>();
    }
}
```

## Handler unit test (Moq)

```csharp
using static {{Module}}.Tests.TestSupport;

namespace {{Module}}.Tests.Features;

public class Create{{Aggregate}}HandlerTests
{
    private readonly Mock<I{{Aggregate}}Repository> _repository = new();
    private Create{{Aggregate}}Handler Handler() => new(_repository.Object);

    [Fact]
    public async Task Valid_AddsAndReturnsId()
    {
        {{Aggregate}}? added = null;
        _repository.Setup(r => r.AddAsync(It.IsAny<{{Aggregate}}>(), It.IsAny<CancellationToken>()))
            .Callback<{{Aggregate}}, CancellationToken>((e, _) => added = e);

        var result = await Handler().Handle(new Create{{Aggregate}}Command(Name(), null), CancellationToken.None);

        added.Should().NotBeNull();
        result.Id.Should().Be(added!.Id);
    }
}
```

## Validator test (real localizer)

```csharp
[Fact]
public void EmptyName_FailsWithLocalizedMessage()
{
    using var _ = new UiCulture("ar");
    var validator = new Create{{Aggregate}}CommandValidator(TestSupport.Localizer());

    var result = validator.Validate(new Create{{Aggregate}}Command(LocalizedText.Create("", ""), null));

    result.IsValid.Should().BeFalse();
    result.Errors.Should().Contain(e => e.PropertyName.Contains("Name"));
}
```

## Integration fixture (`{{Module}}.IntegrationTests/{{Module}}PostgresFixture.cs`)

Copy `src/Modules/Catalog/Catalog.IntegrationTests/CatalogPostgresFixture.cs` and rename. What makes it work:

```csharp
public sealed class {{Module}}PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private ServiceProvider _provider = default!;
    public FakeHttpContextService Http { get; } = new();             // copy from Catalog.IntegrationTests

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:starterkit"] = _container.GetConnectionString()
            }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization();
        services.AddDistributedMemoryCache();
        services.AddSingleton<IEnumLocalizationService, EnumLocalizationService>();
        services.AddSingleton<IHttpContextService>(Http);
        services.Add{{Module}}Module(configuration);                    // the REAL registration
        _provider = services.BuildServiceProvider();

        await using var scope = _provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<{{Module}}DbContext>().Database.MigrateAsync(); // real migrations (RLS, raw indexes)
    }

    public async Task DisposeAsync() { await _provider.DisposeAsync(); await _container.DisposeAsync(); }

    public async Task<T> SendAs<T>(Guid? userId, IRequest<T> request, bool admin = false)
    {
        Http.SetUser(userId, admin);
        await using var scope = _provider.CreateAsyncScope();             // one scope = one HTTP request
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }

    public async Task<T> WithDb<T>(Func<{{Module}}DbContext, Task<T>> work)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<{{Module}}DbContext>());
    }
}

[CollectionDefinition(Name)]
public sealed class {{Module}}PostgresCollection : ICollectionFixture<{{Module}}PostgresFixture>
{
    public const string Name = "{{module}}-postgres";
}
```

## Integration test

```csharp
[Collection({{Module}}PostgresCollection.Name)]
public sealed class {{Aggregate}}LifecycleTests({{Module}}PostgresFixture fx)
{
    [Fact]
    public async Task Update_WithStaleVersion_IsRejected_AndNothingChanges()
    {
        var owner = Guid.NewGuid();
        var id = (await fx.SendAs(owner, new Create{{Aggregate}}Command(LocalizedText.Create("A", "أ"), null))).Id;
        await fx.SendAs(owner, new Update{{Aggregate}}Command(id, LocalizedText.Create("First", "أول"), null, Version: 0));

        var act = () => fx.SendAs(owner, new Update{{Aggregate}}Command(id, LocalizedText.Create("Second", "ثاني"), null, Version: 0));

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
        (await fx.WithDb(db => db.{{Aggregate}}s.AsNoTracking().SingleAsync(x => x.Id == id))).Name.En.Should().Be("First");
    }
}
```

## HTTP E2E test

```csharp
[Collection(E2ECollection.Name)]
public class {{Aggregate}}FlowTests(E2ETestFixture fixture)
{
    private const string Route = "/api/v1/{{aggregate}}s";

    [Fact]
    public async Task Anonymous_Create_Returns401()
    {
        var response = await fixture.CreateAnonymousClient().PostAsJsonAsync(Route, new { name = new { en = "A", ar = "أ" } });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task User_CreateThenGet_RoundTrips()
    {
        var client = fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.User);
        var create = await client.PostAsJsonAsync(Route, new { name = new { en = "A", ar = "أ" } });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        // ... read the envelope (see ProductCrudFlowTests.Envelope<T>) and GET it back
    }
}
```

When the module adds a project reference, add `src/Modules/{{Module}}/{{Module}}.Contracts` to
`src/Api.E2ETests/Api.E2ETests.csproj` so the tests can use its DTOs.
