# C# Docker Secrets Implementation

## Overview

Secrets are handled **entirely in C# code** using a custom `IConfigurationProvider` - no bash scripts needed!

---

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│ Host VPS                                                │
│  ./secrets/postgres_password.txt                       │
│  ./secrets/redis_password.txt                          │
│  ./secrets/jwt_secret.txt                              │
│                                                         │
│  ┌────────────────────────────────────────────────┐    │
│  │ Docker Container                                │    │
│  │                                                │    │
│  │  /run/secrets/postgres_password  ← mounted     │    │
│  │  /run/secrets/redis_password     ← mounted     │    │
│  │  /run/secrets/jwt_secret         ← mounted     │    │
│  │                                                │    │
│  │  ┌──────────────────────────────────────┐      │    │
│  │  │ .NET Application                     │      │    │
│  │  │                                      │      │    │
│  │  │  Program.cs:                         │      │    │
│  │  │    builder.Configuration             │      │    │
│  │  │      .AddDockerSecrets() ───┐        │      │    │
│  │  │                             │        │      │    │
│  │  │  DockerSecretsProvider:     │        │      │    │
│  │  │    1. Read /run/secrets/*  ←┘        │      │    │
│  │  │    2. Build ConnectionStrings        │      │    │
│  │  │    3. Add to IConfiguration          │      │    │
│  │  │                                      │      │    │
│  │  │  Modules access via:                 │      │    │
│  │  │    IConfiguration["ConnectionStrings │      │    │
│  │  │                    :starterkit"]         │      │    │
│  │  └──────────────────────────────────────┘      │    │
│  └────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────┘
```

---

## Implementation

### 1. DockerSecretsConfigurationProvider.cs

Located: `src/Api/Infrastructure/DockerSecretsConfigurationProvider.cs`

```csharp
public class DockerSecretsConfigurationProvider : ConfigurationProvider
{
    private const string SecretsPath = "/run/secrets";

    public override void Load()
    {
        // Check if /run/secrets/ exists
        if (!Directory.Exists(SecretsPath))
        {
            // Not running with Docker secrets - use env vars instead
            return;
        }

        // Read postgres_password
        var postgresPassword = ReadSecret("postgres_password");
        if (!string.IsNullOrEmpty(postgresPassword))
        {
            // Build connection string
            var connectionString =
                $"Host={host};Port={port};Database={db};" +
                $"Username={user};Password={postgresPassword};...";

            // Add to configuration
            Data["ConnectionStrings:starterkit"] = connectionString;
        }

        // Same for Redis and JWT...
    }

    private string? ReadSecret(string secretName)
    {
        var path = Path.Combine(SecretsPath, secretName);
        return File.Exists(path)
            ? File.ReadAllText(path).Trim()
            : null;
    }
}
```

**Key Features:**
- ✅ Type-safe (C# not bash)
- ✅ Integrated with IConfiguration pipeline
- ✅ Automatic fallback if secrets missing
- ✅ Detailed logging with emojis
- ✅ Validates secret strength
- ✅ No shell scripting complexity

### 2. Program.cs Integration

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add Docker Secrets support
builder.Configuration.AddDockerSecrets();

// Configuration hierarchy:
// 1. appsettings.json (base)
// 2. appsettings.Docker.json (if ASPNETCORE_ENVIRONMENT=Docker)
// 3. DockerSecrets (reads /run/secrets/)
// 4. Environment variables (highest priority)

var configuration = builder.Configuration;
```

**That's it!** One line of code.

---

## Configuration Priority

```
Low Priority                                    High Priority
│                                                           │
├─ appsettings.json                                        │
│    "ConnectionStrings": {                                │
│      "starterkit": "Host=localhost;..."  ← Base defaults    │
│    }                                                     │
│                                                          │
├─ appsettings.Docker.json                                │
│    "ConnectionStrings": {                               │
│      "starterkit": "Host=postgres;..."  ← Docker overrides  │
│    }                                                     │
│                                                          │
├─ DockerSecrets Provider ★ NEW                           │
│    Data["ConnectionStrings:starterkit"] =                   │
│      "Host=postgres;Password=<from secret>;..."         │
│    ↑ Overwrites appsettings values                      │
│                                                          │
└─ Environment Variables                                  │
     ConnectionStrings__starterkit=...  ← Highest priority    │
```

---

## Startup Logs

When app starts with Docker secrets, you see:

```
info: Microsoft.Hosting.Lifetime[0]
      Now listening on: http://[::]:80

info: Api.Infrastructure.DockerSecretsConfigurationProvider[0]
      ════════════════════════════════════════════════════════
        Docker Secrets Configuration Provider
      ════════════════════════════════════════════════════════

info: Api.Infrastructure.DockerSecretsConfigurationProvider[0]
      🔐 Reading Docker secrets from /run/secrets/...

info: Api.Infrastructure.DockerSecretsConfigurationProvider[0]
        ✅ Loaded postgres_password (32 chars)
        ✅ Built ConnectionStrings:starterkit
           Host: postgres:5432
           Database: starterkit
           Username: starterkit_user

info: Api.Infrastructure.DockerSecretsConfigurationProvider[0]
        ✅ Loaded redis_password (32 chars)
        ✅ Built ConnectionStrings:Redis
           Host: redis:6379

info: Api.Infrastructure.DockerSecretsConfigurationProvider[0]
        ✅ Loaded jwt_secret (64 chars)
        ✅ Set JwtSettings:SecretKey

info: Api.Infrastructure.DockerSecretsConfigurationProvider[0]
      ════════════════════════════════════════════════════════
        ✅ Docker Secrets Loaded Successfully
      ════════════════════════════════════════════════════════

info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.
```

---

## Why This Is Better Than Bash

| Aspect | Bash Script | C# Provider |
|--------|-------------|-------------|
| **Language** | Shell scripting | C# (type-safe) |
| **Integration** | External process | Native IConfiguration |
| **Debugging** | Hard (shell output) | Easy (breakpoints, logging) |
| **Testing** | Difficult | Unit testable |
| **Error handling** | Exit codes | Exceptions |
| **IDE support** | None | Full IntelliSense |
| **Cross-platform** | Shell differences | Consistent |
| **Maintainability** | Low (bash expertise) | High (.NET developers) |

---

## Development vs Production

### Production (Docker Secrets)

```bash
# Secrets exist
ls /run/secrets/
# postgres_password
# redis_password
# jwt_secret

# Provider reads them
# ✅ Loaded postgres_password (32 chars)
# ✅ Built ConnectionStrings:starterkit
```

### Development (No Secrets)

```bash
# No secrets directory
ls /run/secrets/
# ls: /run/secrets/: No such file or directory

# Provider skips gracefully
# ⚠️  /run/secrets not found - not running with Docker secrets
# ℹ️  Using environment variables or appsettings.json instead

# Falls back to docker-compose.override.yml env vars
```

---

## How Modules Access Secrets

Modules don't know about secrets - they just use `IConfiguration`:

```csharp
// In IdentityModule.cs
public static IServiceCollection AddIdentityModule(
    this IServiceCollection services,
    IConfiguration configuration)
{
    // Get connection string (built from secret or appsettings)
    var connectionString = configuration
        .GetConnectionString("starterkit");

    services.AddDbContext<IdentityDbContext>(options =>
        options.UseNpgsql(connectionString));

    // Get JWT settings (secret from /run/secrets/jwt_secret)
    services.Configure<JwtOptions>(
        configuration.GetSection("JwtSettings"));

    return services;
}
```

**Secrets are transparent!** Modules work the same in dev and prod.

---

## Testing

### Unit Test the Provider

```csharp
[Fact]
public void Load_WithSecretsDirectory_BuildsConnectionStrings()
{
    // Arrange
    var provider = new DockerSecretsConfigurationProvider();

    // Act
    provider.Load();

    // Assert
    provider.TryGet("ConnectionStrings:starterkit", out var connString);
    Assert.Contains("Password=", connString);
}
```

### Integration Test

```csharp
[Fact]
public void Configuration_LoadsDockerSecrets()
{
    var config = new ConfigurationBuilder()
        .AddDockerSecrets()
        .Build();

    var connectionString = config.GetConnectionString("starterkit");

    Assert.NotNull(connectionString);
}
```

---

## Error Handling

### Secret File Missing

```csharp
// Provider handles gracefully
private string? ReadSecret(string secretName)
{
    var path = Path.Combine(SecretsPath, secretName);

    if (!File.Exists(path))
    {
        _logger?.LogWarning("⚠️  {SecretName} not found", secretName);
        return null;  // Graceful degradation
    }

    return File.ReadAllText(path).Trim();
}
```

### Permission Issues

```csharp
try
{
    var secret = File.ReadAllText(secretPath).Trim();
    return string.IsNullOrWhiteSpace(secret) ? null : secret;
}
catch (UnauthorizedAccessException ex)
{
    _logger?.LogError(ex, "❌ Permission denied: {SecretName}", secretName);
    return null;
}
catch (Exception ex)
{
    _logger?.LogError(ex, "❌ Error reading secret: {SecretName}", secretName);
    return null;
}
```

---

## Advantages

### ✅ Type Safety

```csharp
// Strongly-typed access
var jwtOptions = configuration
    .GetSection("JwtSettings")
    .Get<JwtOptions>();

// Not just strings like bash
```

### ✅ Debugging

```csharp
// Set breakpoint in Load() method
public override void Load()
{
    // Step through secret reading
    var secret = ReadSecret("postgres_password");
    // Inspect 'secret' value in debugger
}
```

### ✅ Logging Integration

```csharp
// Uses ILogger (same as rest of app)
_logger?.LogInformation("✅ Loaded {SecretName}", secretName);

// Logs go to Seq, Console, etc.
// Not buried in Docker logs
```

### ✅ No External Dependencies

```dockerfile
# Dockerfile is simple
ENTRYPOINT ["dotnet", "Api.dll"]

# No bash, no sh, no shell scripting
# Pure .NET
```

---

## Deployment

Same as before, but simpler:

```bash
# 1. Generate secrets
./deployment/secrets-setup.sh

# 2. Deploy
docker compose build api
docker compose up -d

# 3. Check logs
docker compose logs api | grep "🔐"
```

**No entrypoint script to debug!**

---

## Summary

| Feature | Implementation |
|---------|----------------|
| **Language** | Pure C# |
| **Integration** | `IConfigurationProvider` |
| **Entry Point** | `builder.Configuration.AddDockerSecrets()` |
| **Secret Reading** | `File.ReadAllText("/run/secrets/...")` |
| **Connection Strings** | Built in C# |
| **Logging** | ILogger with emojis |
| **Error Handling** | C# exceptions |
| **Testing** | Unit testable |
| **Debugging** | Full IDE support |

---

**Result:** Production-grade security with .NET best practices. No bash required!

**Last Updated:** 2026-01-30
