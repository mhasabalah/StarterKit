using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Core.Data;

namespace Api.Tests.Architecture;

/// <summary>
/// RULE: background code that saves entities must publish their domain events.
/// <para>
/// Domain events are published by <c>DispatchDomainEventsBehavior</c> only for mediator commands. Hangfire jobs, hosted
/// services and orchestrators run OUTSIDE the mediator, so a job that calls <c>SaveChanges</c> without the dispatcher
/// silently drops every event it raises (the original bug: ProcessExpiredSubscriptionsJob never sent the
/// "subscription expired" email).
/// </para>
/// A "background type" is a class whose name ends with <c>Job</c> or <c>Orchestrator</c>, or that implements
/// <see cref="IHostedService"/> or is a MassTransit consumer (<c>IConsumer&lt;T&gt;</c>). Such a type must take <see cref="IUnitOfWorkRunner"/> (preferred: fresh scope per
/// unit of work, events published after success) or <see cref="IDomainEventDispatcher"/> (when Hangfire already
/// injected its scoped dependencies) in its constructor when it
/// <list type="number">
/// <item>calls any <c>SaveChanges</c>/<c>SaveChangesAsync</c> (DbContext or repository; found by scanning the IL of
/// the type, its lambdas and async state machines), or</item>
/// <item>creates DI scopes itself (<see cref="IServiceScopeFactory"/> / <see cref="IServiceProvider"/> dependency) and
/// resolves a DbContext or repository from them (<c>GetRequiredService&lt;T&gt;</c>): hand-rolled scopes are exactly
/// how the events got lost - use <see cref="IUnitOfWorkRunner.RunAsync"/>.</item>
/// </list>
/// Limitation: a save hidden behind a repository method with another name (e.g. <c>UpdateStatusAsync</c>) is not
/// detected; such repositories must not persist aggregates that raise domain events, or the job must use the runner.
/// </summary>
public sealed class BackgroundJobEventDispatchTests
{
    /// <summary>Every module implementation (Notification included: its jobs persist delivery logs).</summary>
    private static readonly string[] ScannedAssemblies = ModuleBoundaryTests.Modules;

    /// <summary>
    /// Temporary, explicit exceptions. Each entry must name who removes it. Do not add new ones for new code.
    /// </summary>
    private static readonly Dictionary<string, string> KnownViolations = new();

    private static readonly Type[] DispatchingDependencies = [typeof(IUnitOfWorkRunner), typeof(IDomainEventDispatcher)];
    private static readonly Type[] ScopeCreatingDependencies = [typeof(IServiceScopeFactory), typeof(IServiceProvider)];

    [Fact]
    public void Background_types_that_save_or_create_scopes_publish_their_domain_events()
    {
        var violations = BackgroundTypes()
            .Where(t => !KnownViolations.ContainsKey(t.FullName!))
            .Where(t => !DependsOnAny(t, DispatchingDependencies))
            .Select(t => (Type: t, Reasons: Reasons(t)))
            .Where(x => x.Reasons.Count > 0)
            .Select(x => $"{x.Type.FullName}: {string.Join("; ", x.Reasons)}")
            .ToList();

        violations.Should().BeEmpty(
            "background code runs outside the mediator, so it must take IUnitOfWorkRunner (or IDomainEventDispatcher) " +
            "or the domain events it raises are never published");
    }

    [Fact]
    public void Known_violations_are_still_real_so_the_list_shrinks()
    {
        var types = BackgroundTypes().ToDictionary(t => t.FullName!);

        foreach (var (name, owner) in KnownViolations)
        {
            types.Should().ContainKey(name, $"stale KnownViolations entry ({owner}) - the type no longer exists");
            var type = types[name];
            (DependsOnAny(type, DispatchingDependencies) || Reasons(type).Count == 0)
                .Should().BeFalse($"{name} is compliant now - remove it from KnownViolations ({owner})");
        }
    }

    [Theory]
    [InlineData("Identity", "Identity.Domains.Onboarding.Jobs.OnboardingTimeoutJob")]
    [InlineData("Identity", "Identity.Domains.Onboarding.Jobs.OnboardingOtpRetryJob")]
    [InlineData("Subscriptions", "Subscriptions.Domains.Subscriptions.Services.RenewalOrchestrator")]
    [InlineData("Subscriptions", "Subscriptions.Domains.Subscriptions.Jobs.ProcessPlanPriceChangesJob")]
    [InlineData("Subscriptions", "Subscriptions.Domains.Subscriptions.Jobs.ProcessUpgradeSlaExpiryJob")]
    [InlineData("Subscriptions", "Subscriptions.Infrastructure.PlanSeedService")]
    public void The_save_detector_sees_saves_in_lambdas_and_async_methods(string assembly, string typeName)
    {
        // Guards the guard: if the IL scan broke, the main rule would pass vacuously.
        var type = Assembly.Load(assembly).GetType(typeName, throwOnError: true)!;

        CallsSaveChanges(type).Should().BeTrue();
        IsBackgroundType(type).Should().BeTrue();
    }

    [Theory]
    [InlineData("Tenancy", "Tenancy.Services.TenantBillingStatusConsumer")]
    [InlineData("Identity", "Identity.Domains.Users.Consumers.UserAvatarChangedConsumer")]
    public void MassTransit_consumers_count_as_background_types(string assembly, string typeName)
    {
        var type = Assembly.Load(assembly).GetType(typeName, true)!;

        IsBackgroundType(type).Should().BeTrue("consumers run on the bus, outside the mediator");
        BackgroundTypes().Should().Contain(type);
    }

    [Fact]
    public void The_scope_detector_sees_persistence_resolved_from_hand_rolled_scopes()
    {
        // MalwareScannerStartupCheck creates a scope only to resolve a scanner - not persistence.
        var scanner = Assembly.Load("Attachment").GetType("Attachment.Infrastructure.Validation.MalwareScannerStartupCheck", true)!;
        ResolvesPersistence(scanner).Should().BeFalse();

        var orchestrator = Assembly.Load("Subscriptions").GetType("Subscriptions.Domains.Subscriptions.Services.RenewalOrchestrator", true)!;
        ResolvesPersistence(orchestrator).Should().BeTrue();
    }

    [Fact]
    public void The_save_detector_does_not_flag_read_only_jobs()
    {
        var type = Assembly.Load("Identity").GetType("Identity.Infrastructure.BackgroundJobs.RefreshTokenCleanupJob", true)!;

        CallsSaveChanges(type).Should().BeFalse("it deletes set-based (ExecuteDelete) without SaveChanges");
    }

    // ----- helpers -----

    private static IEnumerable<Type> BackgroundTypes() =>
        ScannedAssemblies
            .Select(Assembly.Load)
            .SelectMany(a => a.GetTypes())
            .Where(IsBackgroundType);

    private static bool IsBackgroundType(Type t) =>
        t is { IsClass: true, IsAbstract: false } &&
        !t.IsDefined(typeof(CompilerGeneratedAttribute), false) &&
        (t.Name.EndsWith("Job", StringComparison.Ordinal) ||
         t.Name.EndsWith("Orchestrator", StringComparison.Ordinal) ||
         typeof(IHostedService).IsAssignableFrom(t) ||
         IsMassTransitConsumer(t));

    /// <summary>MassTransit consumers run on the bus, outside the mediator: nothing publishes their domain events for them.</summary>
    private static bool IsMassTransitConsumer(Type t) =>
        t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(MassTransit.IConsumer<>));

    private static List<string> Reasons(Type t)
    {
        var reasons = new List<string>();
        if (CallsSaveChanges(t))
            reasons.Add("calls SaveChanges");
        if (DependsOnAny(t, ScopeCreatingDependencies) && ResolvesPersistence(t))
            reasons.Add("resolves a DbContext/repository from scopes it creates itself (use IUnitOfWorkRunner)");
        return reasons;
    }

    private static bool DependsOnAny(Type t, Type[] dependencies) =>
        t.GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Any(p => dependencies.Contains(p.ParameterType));

    private static bool CallsSaveChanges(Type type) =>
        AllCalls(type).Any(c => c.Name is "SaveChanges" or "SaveChangesAsync");

    /// <summary><c>GetRequiredService&lt;T&gt;</c> / <c>GetService&lt;T&gt;</c> with T a DbContext or a repository.</summary>
    private static bool ResolvesPersistence(Type type) =>
        AllCalls(type)
            .OfType<MethodInfo>()
            .Where(m => m.IsGenericMethod && m.Name is "GetRequiredService" or "GetService")
            .Select(m => m.GetGenericArguments()[0])
            .Any(t => typeof(Microsoft.EntityFrameworkCore.DbContext).IsAssignableFrom(t) ||
                      t.Name.EndsWith("Repository", StringComparison.Ordinal));

    private static IEnumerable<MethodBase> AllCalls(Type type) =>
        SelfAndNested(type)
            .SelectMany(t => t.GetMethods(AllDeclared).Cast<MethodBase>().Concat(t.GetConstructors(AllDeclared)))
            .SelectMany(CalledMethods);

    private const BindingFlags AllDeclared =
        BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static IEnumerable<Type> SelfAndNested(Type type) =>
        new[] { type }.Concat(type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).SelectMany(SelfAndNested));

    // ----- minimal IL reader: yields the targets of call / callvirt / newobj / ldftn -----

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => o.Value);

    private static IEnumerable<MethodBase> CalledMethods(MethodBase method)
    {
        byte[]? il;
        try
        {
            il = method.GetMethodBody()?.GetILAsByteArray();
        }
        catch
        {
            yield break;
        }

        if (il is null) yield break;

        var position = 0;
        while (position < il.Length)
        {
            short value = il[position++];
            if (value == 0xFE && position < il.Length)
                value = unchecked((short)(0xFE00 | il[position++]));

            if (!OpCodesByValue.TryGetValue(value, out var opCode))
                yield break; // unknown opcode: stop rather than misread operands

            if (opCode.OperandType == OperandType.InlineMethod)
            {
                var token = BitConverter.ToInt32(il, position);
                MethodBase? target = null;
                try
                {
                    target = method.Module.ResolveMethod(
                        token,
                        method.DeclaringType?.IsGenericType == true ? method.DeclaringType.GetGenericArguments() : null,
                        method.IsGenericMethod ? method.GetGenericArguments() : null);
                }
                catch
                {
                    // Unresolvable (e.g. open generic context) - ignore.
                }

                if (target is not null) yield return target;
            }

            position += OperandSize(opCode, il, position);
        }
    }

    private static int OperandSize(OpCode opCode, byte[] il, int position) => opCode.OperandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, position),
        _ => 4
    };
}
