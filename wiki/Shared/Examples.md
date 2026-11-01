# HttpContextService - Examples & Tests

Real-world usage examples and unit test patterns for `IHttpContextService`.

---

## Example 1: Basic Usage in Command Handler

```csharp
public record CreateAgentCommand(string ArabicName, string EnglishName) : ICommand<Result>;

public class CreateAgentHandler(
    IAgentRepository repository,
    IHttpContextService httpContextService)
    : ICommandHandler<CreateAgentCommand, Result>
{
    public async Task<Result> Handle(CreateAgentCommand command, CancellationToken cancellationToken)
    {
        // Get current user information
        var userId = httpContextService.GetUserId();
        var userName = httpContextService.GetUserName();
        var ipAddress = httpContextService.GetIpAddress();

        // Create agent with user context
        var agent = Agent.Create(
            Guid.NewGuid(),
            command.ArabicName,
            command.EnglishName
        );

        await repository.AddAsync(agent, cancellationToken);

        // Note: CreatedBy and CreatedAt are automatically set by AuditableEntityInterceptor
        return Result.Success();
    }
}
```

---

## Example 2: Role-Based Authorization

```csharp
public record DeleteAgentCommand(Guid Id) : ICommand<Result>;

public class DeleteAgentHandler(
    IAgentRepository repository,
    IHttpContextService httpContextService)
    : ICommandHandler<DeleteAgentCommand, Result>
{
    public async Task<Result> Handle(DeleteAgentCommand command, CancellationToken cancellationToken)
    {
        // Check if user has admin or manager role
        if (!httpContextService.HasAnyRole("Admin", "Manager"))
        {
            return Result.Failure(Error.Forbidden("Only admins and managers can delete agents"));
        }

        var agent = await repository.GetByIdAsync(command.Id, cancellationToken);
        if (agent == null)
        {
            return Result.Failure(Error.NotFound("Agent not found"));
        }

        // Additional check: Users can only delete their own agents unless they're admin
        var currentUserId = httpContextService.GetUserId();
        var isAdmin = httpContextService.HasAnyRole("Admin");

        if (agent.CreatedBy != currentUserId && !isAdmin)
        {
            return Result.Failure(Error.Forbidden("You can only delete your own agents"));
        }

        await repository.DeleteAsync(agent, cancellationToken);
        return Result.Success();
    }
}
```

---

## Example 3: Audit Logging

```csharp
public class AuditService(
    IHttpContextService httpContextService,
    ILogger<AuditService> logger)
{
    public void LogAction(string action, string entityType, string entityId, object? additionalData = null)
    {
        // Get comprehensive audit information
        var auditInfo = httpContextService.GetAuditInfo();
        var requestContext = httpContextService.GetRequestContext();

        logger.LogInformation(
            "Audit Log: Action={Action} | Entity={EntityType}#{EntityId} | " +
            "User={UserId}({UserName}) | Email={Email} | IP={IpAddress} | " +
            "UserAgent={UserAgent} | CorrelationId={CorrelationId} | " +
            "RequestPath={RequestPath} | RequestMethod={RequestMethod} | " +
            "Timestamp={Timestamp}",
            action,
            entityType,
            entityId,
            auditInfo.UserId,
            auditInfo.UserName,
            auditInfo.Email,
            auditInfo.IpAddress,
            auditInfo.UserAgent,
            requestContext.CorrelationId,
            requestContext.Path,
            requestContext.Method,
            auditInfo.Timestamp
        );

        if (additionalData != null)
        {
            logger.LogDebug("Additional Data: {@AdditionalData}", additionalData);
        }
    }
}
```

---

## Example 4: Multi-Tenant Query Filtering

```csharp
public record GetAgentsQuery : IQuery<List<AgentDto>>;

public class GetAgentsHandler(
    IAgentRepository repository,
    IHttpContextService httpContextService)
    : IQueryHandler<GetAgentsQuery, List<AgentDto>>
{
    public async Task<List<AgentDto>> Handle(GetAgentsQuery query, CancellationToken cancellationToken)
    {
        // Get tenant ID from claim or header
        var tenantId = httpContextService.GetTenantId();
        
        if (string.IsNullOrEmpty(tenantId))
        {
            throw new InvalidOperationException("Tenant ID is required");
        }

        // Apply tenant filter automatically
        var agents = await repository
            .GetAllAsync(a => a.TenantId == tenantId, cancellationToken);
            
        return agents.Adapt<List<AgentDto>>();
    }
}
```

---

## Example 5: IP-Based Security

```csharp
public class IpWhitelistMiddleware(
    IHttpContextService httpContextService,
    IConfiguration configuration,
    RequestDelegate next)
{
    private readonly string[] _allowedIpAddresses = 
        configuration.GetSection("Security:AllowedIpAddresses").Get<string[]>() ?? Array.Empty<string>();

    public async Task InvokeAsync(HttpContext context)
    {
        var ipAddress = httpContextService.GetIpAddress();
        
        if (ipAddress == null || !_allowedIpAddresses.Contains(ipAddress))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Access denied: IP not whitelisted");
            return;
        }

        await next(context);
    }
}
```

---

## Example 6: Claims-Based Feature Toggle

```csharp
public class FeatureToggleService(IHttpContextService httpContextService)
{
    public bool IsFeatureEnabled(string featureName)
    {
        // Check for feature flag claim
        var featureClaims = httpContextService.GetClaimValues("feature");
        return featureClaims.Contains(featureName);
    }
    
    public bool CanAccessBetaFeatures()
    {
        return httpContextService.HasClaim("beta_access", "true") ||
               httpContextService.HasAnyRole("Admin", "BetaTester");
    }
}
```

---

## Example 7: Request Context Logging Middleware

```csharp
public class RequestContextLoggingMiddleware(
    IHttpContextService httpContextService,
    ILogger<RequestContextLoggingMiddleware> logger,
    RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var requestContext = httpContextService.GetRequestContext();
        
        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = requestContext.CorrelationId ?? "N/A",
            ["UserId"] = httpContextService.GetUserId() ?? "anonymous",
            ["IpAddress"] = requestContext.IpAddress ?? "unknown",
            ["RequestPath"] = requestContext.Path ?? ""
        }))
        {
            logger.LogInformation(
                "Request started: {Method} {Path} from {IP}",
                requestContext.Method,
                requestContext.Path,
                requestContext.IpAddress
            );

            await next(context);

            logger.LogInformation(
                "Request completed: {Method} {Path} with {StatusCode}",
                requestContext.Method,
                requestContext.Path,
                context.Response.StatusCode
            );
        }
    }
}
```

---

## Example 8: User Activity Tracking

```csharp
public class UserActivityTracker(
    IHttpContextService httpContextService,
    IUserActivityRepository activityRepository)
{
    public async Task TrackActivityAsync(string activityType, string description)
    {
        if (!httpContextService.IsAuthenticated())
        {
            return; // Don't track anonymous users
        }

        var activity = new UserActivity
        {
            UserId = httpContextService.GetUserIdAsGuid() ?? Guid.Empty,
            ActivityType = activityType,
            Description = description,
            IpAddress = httpContextService.GetIpAddress(),
            UserAgent = httpContextService.GetUserAgent(),
            RequestPath = httpContextService.GetRequestPath(),
            Timestamp = DateTime.UtcNow
        };

        await activityRepository.AddAsync(activity);
    }
}
```

---

## Example 9: Background Job with Fallback

```csharp
public class BackgroundJobService(
    IHttpContextService httpContextService,
    ILogger<BackgroundJobService> logger)
{
    public async Task ProcessJobAsync()
    {
        // Background jobs don't have HTTP context
        var userId = httpContextService.GetUserId() ?? "system";
        var userName = httpContextService.GetUserName() ?? "Background Job";

        logger.LogInformation(
            "Processing job as {UserName} ({UserId})",
            userName,
            userId
        );

        // Job logic...
    }
}
```

---

## Example 10: Custom Authorization Handler

```csharp
public class ResourceOwnerAuthorizationHandler(
    IHttpContextService httpContextService) 
    : AuthorizationHandler<ResourceOwnerRequirement, Resource>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ResourceOwnerRequirement requirement,
        Resource resource)
    {
        var currentUserId = httpContextService.GetUserId();
        
        // Check if user owns the resource or is admin
        if (resource.OwnerId == currentUserId || 
            httpContextService.HasAnyRole("Admin"))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
```

---

# Unit Tests

## Test 1: Basic User ID Retrieval

```csharp
[Fact]
public void GetUserId_WhenUserIsAuthenticated_ReturnsUserId()
{
    // Arrange
    var userId = "12345";
    var claims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, userId)
    };
    var identity = new ClaimsIdentity(claims, "TestAuth");
    var principal = new ClaimsPrincipal(identity);
    
    var mockHttpContext = new Mock<HttpContext>();
    mockHttpContext.Setup(x => x.User).Returns(principal);
    
    var mockAccessor = new Mock<IHttpContextAccessor>();
    mockAccessor.Setup(x => x.HttpContext).Returns(mockHttpContext.Object);
    
    var service = new HttpContextService(mockAccessor.Object);

    // Act
    var result = service.GetUserId();

    // Assert
    result.Should().Be(userId);
}
```

---

## Test 2: User ID as Long

```csharp
[Fact]
public void GetUserIdAsLong_WhenValidLongId_ReturnsLongValue()
{
    // Arrange
    var userId = "12345";
    var claims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, userId)
    };
    var identity = new ClaimsIdentity(claims, "TestAuth");
    var principal = new ClaimsPrincipal(identity);
    
    var mockHttpContext = new Mock<HttpContext>();
    mockHttpContext.Setup(x => x.User).Returns(principal);
    
    var mockAccessor = new Mock<IHttpContextAccessor>();
    mockAccessor.Setup(x => x.HttpContext).Returns(mockHttpContext.Object);
    
    var service = new HttpContextService(mockAccessor.Object);

    // Act
    var result = service.GetUserIdAsLong();

    // Assert
    result.Should().Be(12345L);
}
```

---

## Test 3: No HTTP Context

```csharp
[Fact]
public void GetUserId_WhenNoHttpContext_ReturnsNull()
{
    // Arrange
    var mockAccessor = new Mock<IHttpContextAccessor>();
    mockAccessor.Setup(x => x.HttpContext).Returns((HttpContext?)null);
    
    var service = new HttpContextService(mockAccessor.Object);

    // Act
    var result = service.GetUserId();

    // Assert
    result.Should().BeNull();
}
```

---

## Test 4: Role Check

```csharp
[Fact]
public void HasAnyRole_WhenUserHasRole_ReturnsTrue()
{
    // Arrange
    var claims = new List<Claim>
    {
        new Claim(ClaimTypes.Role, "Admin"),
        new Claim(ClaimTypes.Role, "User")
    };
    var identity = new ClaimsIdentity(claims, "TestAuth");
    var principal = new ClaimsPrincipal(identity);
    
    var mockHttpContext = new Mock<HttpContext>();
    mockHttpContext.Setup(x => x.User).Returns(principal);
    
    var mockAccessor = new Mock<IHttpContextAccessor>();
    mockAccessor.Setup(x => x.HttpContext).Returns(mockHttpContext.Object);
    
    var service = new HttpContextService(mockAccessor.Object);

    // Act
    var result = service.HasAnyRole("Admin", "Manager");

    // Assert
    result.Should().BeTrue();
}
```

---

## Test 5: IP Address Detection

```csharp
[Fact]
public void GetIpAddress_WhenXForwardedForHeaderExists_ReturnsFirstIp()
{
    // Arrange
    var mockHttpContext = new Mock<HttpContext>();
    var mockRequest = new Mock<HttpRequest>();
    var headers = new HeaderDictionary
    {
        ["X-Forwarded-For"] = "192.168.1.100, 10.0.0.1"
    };
    
    mockRequest.Setup(x => x.Headers).Returns(headers);
    mockHttpContext.Setup(x => x.Request).Returns(mockRequest.Object);
    
    var mockAccessor = new Mock<IHttpContextAccessor>();
    mockAccessor.Setup(x => x.HttpContext).Returns(mockHttpContext.Object);
    
    var service = new HttpContextService(mockAccessor.Object);

    // Act
    var result = service.GetIpAddress();

    // Assert
    result.Should().Be("192.168.1.100");
}
```

---

## Test 6: Mock in Handler Test

```csharp
[Fact]
public async Task Handle_WithAuthenticatedUser_CreatesAgent()
{
    // Arrange
    var mockHttpContextService = new Mock<IHttpContextService>();
    mockHttpContextService.Setup(x => x.GetUserId()).Returns("test-user-123");
    mockHttpContextService.Setup(x => x.GetUserName()).Returns("testuser");
    mockHttpContextService.Setup(x => x.IsAuthenticated()).Returns(true);
    mockHttpContextService.Setup(x => x.GetIpAddress()).Returns("192.168.1.1");
    
    var mockRepository = new Mock<IAgentRepository>();
    
    var handler = new CreateAgentHandler(
        mockRepository.Object,
        mockHttpContextService.Object
    );
    
    var command = new CreateAgentCommand("Arabic Name", "English Name");
    
    // Act
    var result = await handler.Handle(command, CancellationToken.None);
    
    // Assert
    result.IsSuccess.Should().BeTrue();
    mockRepository.Verify(x => x.AddAsync(It.IsAny<Agent>(), It.IsAny<CancellationToken>()), Times.Once);
}
```

---

## Test 7: Authorization Failure

```csharp
[Fact]
public async Task Handle_WithoutAdminRole_ReturnsForbidden()
{
    // Arrange
    var mockHttpContextService = new Mock<IHttpContextService>();
    mockHttpContextService.Setup(x => x.HasAnyRole("Admin", "Manager")).Returns(false);
    
    var mockRepository = new Mock<IAgentRepository>();
    
    var handler = new DeleteAgentHandler(
        mockRepository.Object,
        mockHttpContextService.Object
    );
    
    var command = new DeleteAgentCommand(Guid.NewGuid());
    
    // Act
    var result = await handler.Handle(command, CancellationToken.None);
    
    // Assert
    result.IsSuccess.Should().BeFalse();
    result.Error.Type.Should().Be(ErrorType.Forbidden);
}
```

---

## Summary

These examples demonstrate:
- ✅ Basic user information access
- ✅ Role-based authorization
- ✅ Comprehensive audit logging
- ✅ Multi-tenant filtering
- ✅ IP-based security
- ✅ Claims-based features
- ✅ Request context tracking
- ✅ Background job handling
- ✅ Custom authorization
- ✅ Complete unit test patterns

For more information, see [README.md](README.md).
