using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Hosting;

namespace Api.Tests.Architecture;

/// <summary>
/// The REAL application container, without a database or server: Program's top-level statements run until
/// <c>builder.Build()</c> (the same trick EF tooling and WebApplicationFactory use) and are stopped right after.
/// Shared by architecture tests that inspect the real registrations (endpoints, policies, EF models).
/// </summary>
internal static class RealHost
{
    private static readonly Lazy<IServiceProvider> Lazy = new(Build);

    public static IServiceProvider Services => Lazy.Value;

    private static IServiceProvider Build()
    {
        var captured = new TaskCompletionSource<IServiceProvider>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = DiagnosticListener.AllListeners.Subscribe(new HostCapture(captured));

        var entryPoint = typeof(Program).Assembly.EntryPoint!;
        string[] args =
        [
            "--environment=Testing",
            "--ConnectionStrings:starterkit=Host=localhost;Database=never-opened;Username=x;Password=x",
            "--Redis:Enabled=false",
            "--JwtSettings:SecretKey=architecture-tests-signing-key-0123456789",
            "--JwtSettings:Issuer=tests",
            "--JwtSettings:Audience=tests"
        ];

        var thread = new Thread(() =>
        {
            try
            {
                entryPoint.Invoke(null, [args]);
                captured.TrySetException(new InvalidOperationException("Program finished without building a host."));
            }
            catch (Exception ex) when (Unwrap(ex) is StopTheHostException)
            {
                // Expected: stopped right after Build().
            }
            catch (Exception ex)
            {
                captured.TrySetException(Unwrap(ex));
            }
        });
        thread.Start();

        if (!captured.Task.Wait(TimeSpan.FromMinutes(2)))
        {
            throw new TimeoutException("Program did not build its host within 2 minutes.");
        }

        return captured.Task.Result;
    }

    private static Exception Unwrap(Exception ex) =>
        ex is TargetInvocationException { InnerException: { } inner } ? Unwrap(inner)
        : ex is AggregateException { InnerException: { } aggregateInner } ? Unwrap(aggregateInner)
        : ex;

    private sealed class StopTheHostException : Exception;

    private sealed class HostCapture(TaskCompletionSource<IServiceProvider> captured)
        : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>
    {
        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == "Microsoft.Extensions.Hosting")
            {
                listener.Subscribe(this);
            }
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Key == "HostBuilt" && value.Value is IHost host && !captured.Task.IsCompleted)
            {
                captured.TrySetResult(host.Services);
                throw new StopTheHostException();
            }
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }
}
