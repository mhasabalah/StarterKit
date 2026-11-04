# new-module templates

These are trimmed from `src/Modules/Catalog`. If a template and Catalog disagree, Catalog is right; update this file.

## {{Module}}.Contracts/{{Module}}.Contracts.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
    </PropertyGroup>
    <ItemGroup>
        <!-- Thin contract: Shared.Contracts only (no Shared.Core / infrastructure; architecture test). -->
        <ProjectReference Include="..\..\..\Shared\Shared.Contracts\Shared.Contracts.csproj" />
    </ItemGroup>
</Project>
```

## {{Module}}.Contracts/GlobalUsings.cs + {{Module}}ContractsRoot.cs

```csharp
global using Shared.Contracts.Mediation;
global using Shared.Contracts.CQRS;
global using Shared.Contracts.Localization;
global using Shared.Contracts.Pagination;
global using Shared.Contracts.Attributes;
```

```csharp
using System.Reflection;

namespace {{Module}}.Contracts;

/// <summary>Assembly marker for {{Module}}.Contracts (enum lookup registration in src/Api/Program.cs).</summary>
public static class {{Module}}ContractsRoot
{
    public static Assembly AssemblyName => Assembly.GetExecutingAssembly();
}
```

## {{Module}}/{{Module}}.csproj

Versions are central in `Directory.Packages.props`, so never put `Version=` on a PackageReference.

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
    </PropertyGroup>
    <ItemGroup>
        <PackageReference Include="Carter" />
        <PackageReference Include="FluentValidation" />
        <PackageReference Include="Mapster" />
        <PackageReference Include="MassTransit" />
        <PackageReference Include="Microsoft.EntityFrameworkCore" />
        <PackageReference Include="Microsoft.EntityFrameworkCore.Design">
            <PrivateAssets>all</PrivateAssets>
            <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
        </PackageReference>
        <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
        <PackageReference Include="EFCore.NamingConventions" />
        <PackageReference Include="Hangfire.Core" />
    </ItemGroup>
    <ItemGroup>
        <ProjectReference Include="..\..\..\Shared\Shared.Core\Shared.Core.csproj" />
        <ProjectReference Include="..\..\..\Shared\Shared.Messaging\Shared.Messaging.csproj" />
        <ProjectReference Include="..\{{Module}}.Contracts\{{Module}}.Contracts.csproj" />
        <!-- Other modules: ONLY their .Contracts, e.g. ..\..\Attachment\Attachment.Contracts\Attachment.Contracts.csproj -->
    </ItemGroup>
    <ItemGroup>
        <InternalsVisibleTo Include="{{Module}}.Tests" />
        <InternalsVisibleTo Include="{{Module}}.IntegrationTests" />
        <InternalsVisibleTo Include="DynamicProxyGenAssembly2" /> <!-- Moq proxies for internal types -->
    </ItemGroup>
    <ItemGroup>
        <Compile Update="Resources\{{Module}}Resource.Designer.cs">
            <DesignTime>True</DesignTime><AutoGen>True</AutoGen><DependentUpon>{{Module}}Resource.resx</DependentUpon>
        </Compile>
        <EmbeddedResource Update="Resources\{{Module}}Resource.resx">
            <Generator>PublicResXFileCodeGenerator</Generator>
            <LastGenOutput>{{Module}}Resource.Designer.cs</LastGenOutput>
        </EmbeddedResource>
    </ItemGroup>
</Project>
```

Generate `Resources/{{Module}}Resource.Designer.cs` by building in an IDE, or copy `CatalogResource.Designer.cs` and
rename the class and namespace. Add the Arabic file as `Resources/{{Module}}Resource.ar.resx`.

## {{Module}}/GlobalUsings.cs

```csharp
global using System.Reflection;
global using Carter;
global using FluentValidation;
global using Mapster;
global using Shared.Contracts.Mediation;
global using Microsoft.AspNetCore.Builder;
global using Microsoft.AspNetCore.Http;
global using Microsoft.AspNetCore.Mvc;
global using Microsoft.AspNetCore.Routing;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.EntityFrameworkCore.Metadata.Builders;
global using Microsoft.Extensions.Localization;
global using Microsoft.Extensions.Logging;
global using Shared.Contracts.CQRS;
global using Shared.Contracts.Enums;
global using Shared.Contracts.Events;
global using Shared.Contracts.Localization;
global using Shared.Contracts.Pagination;
global using Shared.Core.Authorization;
global using Shared.Core.Data;
global using Shared.Core.Data.Seed;
global using Shared.Core.DDD;
global using Shared.Core.Exceptions;
global using Shared.Core.Extensions;
global using Shared.Core.Localization.Services;
global using Shared.Core.Response;
global using Shared.Core.Web;
global using {{Module}}.Data;
global using {{Module}}.Data.Repository;
global using {{Module}}.Resources;

namespace {{Module}};

public static class {{Module}}Root
{
    public static Assembly AssemblyName => Assembly.GetExecutingAssembly();
}
```

## {{Module}}/Data/{{Module}}DbContext.cs: tenant-data (default)

```csharp
using Shared.Core.Messaging.Outbox;
using Shared.Core.MultiTenancy;

namespace {{Module}}.Data;

/// <summary>DATA PLANE context: every entity is tenant-owned (named Tenant filter + RLS).</summary>
public class {{Module}}DbContext(DbContextOptions<{{Module}}DbContext> options)
    : AppDbContextBase(options), ITenantDataContext
{
    // public DbSet<{{Aggregate}}> {{Aggregate}}s => Set<{{Aggregate}}>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("{{schema}}");
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // builder.FilterSoftDeletedProperties();   // ONLY if an aggregate really needs soft delete
        builder.AddTenantOutbox();                   // only if the module publishes integration events

        builder.ToSnakeCaseTables();
        builder.ConfigureVersionConcurrencyTokens();

        ApplyTenantFilters(builder);                 // MUST be last
    }
}
```

## {{Module}}/Data/{{Module}}DbContext.cs: host (platform / control plane)

```csharp
using Shared.Core.Messaging.Outbox;

namespace {{Module}}.Data;

public class {{Module}}DbContext(DbContextOptions<{{Module}}DbContext> options) : AppDbContextBase(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("{{schema}}");
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        builder.AddOutbox();                         // only if the module publishes integration events
        builder.ToSnakeCaseTables();
        builder.ConfigureVersionConcurrencyTokens();
    }
}
```

## {{Module}}/Data/{{Module}}DbContextFactory.cs

The connection string here is design-time only (`dotnet ef`). It is the local-dev default, not a secret.

```csharp
using Microsoft.EntityFrameworkCore.Design;

namespace {{Module}}.Data;

public class {{Module}}DbContextFactory : IDesignTimeDbContextFactory<{{Module}}DbContext>
{
    public {{Module}}DbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<{{Module}}DbContext>();
        const string connectionString =
            "Server=localhost;Port=5432;Database=starterkit;User Id=postgres;Password=postgres;Include Error Detail=true";
        optionsBuilder.UseNpgsql(connectionString, b => b.UseModuleMigrationsHistory<{{Module}}DbContext>());
        optionsBuilder.UseSnakeCaseNamingConvention();
        return new {{Module}}DbContext(optionsBuilder.Options);
    }
}
```

## {{Module}}/{{Module}}Module.cs

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Core.Mapster;
using Shared.Core.Messaging.Outbox;

namespace {{Module}};

public static class {{Module}}Module
{
    public static IServiceCollection Add{{Module}}Module(this IServiceCollection services, IConfiguration configuration)
    {
        // services.Configure<{{Module}}Options>(configuration.GetSection({{Module}}Options.SectionName));

        // One repository per aggregate root:
        // services.AddScoped<I{{Aggregate}}Repository, {{Aggregate}}Repository>();

        services.AddMediator({{Module}}Root.AssemblyName);
        services.AddCustomMapster({{Module}}Root.AssemblyName);
        services.AddCarterWithAssemblies({{Module}}Root.AssemblyName);
        services.AddCustomDbContext<{{Module}}DbContext>(configuration, "starterkit");
        // services.AddOutbox<{{Module}}DbContext>();         // with builder.AddTenantOutbox()/AddOutbox()
        // services.AddScoped<IDataSeeder, {{Module}}DataSeeder>();
        // services.AddTransient<SomeJob>();                   // Hangfire jobs (see add-background-job)
        return services;
    }

    public static IApplicationBuilder Use{{Module}}Module(this IApplicationBuilder app)
    {
        app.UseMigration<{{Module}}DbContext>();
        // Recurring jobs: see add-background-job
        return app;
    }
}
```

Options class pattern (`{{Module}}/Options/{{Module}}Options.cs`, bound from the `"{{Module}}"` appsettings section):

```csharp
namespace {{Module}}.Options;

public sealed class {{Module}}Options
{
    public const string SectionName = "{{Module}}";
    // public string SomeCron { get; set; } = "0 3 * * *";
}
```

## {{Module}}.Tests/{{Module}}.Tests.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
        <IsPackable>false</IsPackable>
    </PropertyGroup>
    <ItemGroup>
        <PackageReference Include="Microsoft.NET.Test.Sdk" />
        <PackageReference Include="xunit" />
        <PackageReference Include="xunit.runner.visualstudio">
            <PrivateAssets>all</PrivateAssets>
            <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
        </PackageReference>
        <PackageReference Include="FluentAssertions" />
        <PackageReference Include="Moq" />
        <PackageReference Include="coverlet.collector">
            <PrivateAssets>all</PrivateAssets>
            <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
        </PackageReference>
    </ItemGroup>
    <ItemGroup>
        <ProjectReference Include="..\{{Module}}\{{Module}}.csproj" />
        <ProjectReference Include="..\{{Module}}.Contracts\{{Module}}.Contracts.csproj" />
    </ItemGroup>
</Project>
```

## {{Module}}.IntegrationTests/{{Module}}.IntegrationTests.csproj

This is the same as the unit-test project, plus:

```xml
<PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup>
<ItemGroup>
    <PackageReference Include="Testcontainers.PostgreSql" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
</ItemGroup>
<ItemGroup>
    <ProjectReference Include="..\..\..\Shared\Shared.Core\Shared.Core.csproj" />
</ItemGroup>
<ItemGroup>
    <Using Include="Xunit" />
    <Using Include="FluentAssertions" />
</ItemGroup>
```

For the fixture, see the `write-tests` skill (`references/templates.md`, "Integration fixture").

## StarterKit.slnx

```xml
<Folder Name="/src/Modules/{{Module}}/">
  <Project Path="src/Modules/{{Module}}/{{Module}}.Contracts/{{Module}}.Contracts.csproj" />
  <Project Path="src/Modules/{{Module}}/{{Module}}/{{Module}}.csproj" />
  <Project Path="src/Modules/{{Module}}/{{Module}}.Tests/{{Module}}.Tests.csproj" />
  <Project Path="src/Modules/{{Module}}/{{Module}}.IntegrationTests/{{Module}}.IntegrationTests.csproj" />
</Folder>
```

## Removing a module

This is the reverse of the steps above. For the full checklist, see `wiki/Modules/Catalog.md` → *Removing the Catalog
sample*. It covers `Program.cs` (Add/Use, MassTransit, EnumLookup), `Api.csproj`, `Api.E2ETests.csproj` and its test
folder, `ModuleBoundaryTests.Modules`, the `MessagingArchitectureTests` / `TenantIsolationArchitectureTests`
expectations, appsettings, the slnx, `DROP SCHEMA <schema> CASCADE;`, and the module's Hangfire recurring jobs.
