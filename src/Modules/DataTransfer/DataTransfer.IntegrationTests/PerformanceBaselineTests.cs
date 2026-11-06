using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Xunit.Abstractions;
using A = DataTransfer.IntegrationTests.DataTransferPostgresFixture;

namespace DataTransfer.IntegrationTests;

/// <summary>Opt-in: large files take a while. Run with <c>STARTERKIT_PERF_TESTS=1</c> (CI: datatransfer-perf.yml).</summary>
public sealed class PerfFactAttribute : FactAttribute
{
    public const string Variable = "STARTERKIT_PERF_TESTS";

    public PerfFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(Variable) != "1")
            Skip = $"Performance baseline: set {Variable}=1 (Docker required, slow).";
    }
}

/// <summary>
/// Baseline of the ENGINE on real Postgres: streaming parse, header mapping, batching, one unit of work per batch,
/// checkpoint + counters + progress under the lease, then the error report or the export file. The definition is the
/// in-memory test definition, so the numbers measure DataTransfer itself, not a module's own writes.
/// <para>
/// Thresholds (environment, so a slower CI runner can be tuned without a code change):
/// <c>STARTERKIT_PERF_MIN_ROWS_PER_SEC</c> (default 10000; about 64k rows/s on a developer machine) and <c>STARTERKIT_PERF_MAX_HEAP_MB</c> (default 100, the
/// peak growth of the managed heap: streaming must not hold the file in memory). Results are printed and written to
/// <c>TestResults/datatransfer-perf.json</c> for the CI artifact.
/// </para>
/// </summary>
[Collection(DataTransferPostgresCollection.Name)]
public sealed class PerformanceBaselineTests(DataTransferPostgresFixture fx, ITestOutputHelper output) : IAsyncLifetime
{
    private const int Rows = 100_000;

    private static readonly double MinRowsPerSecond = Threshold("STARTERKIT_PERF_MIN_ROWS_PER_SEC", 10_000);
    private static readonly double MaxHeapGrowthMb = Threshold("STARTERKIT_PERF_MAX_HEAP_MB", 100);

    public async Task InitializeAsync()
    {
        await fx.ResetAsync();
        fx.Options.BatchSize = 500; // the production default (the fixture runs tiny batches for the functional tests)
        fx.Options.ExportProgressEvery = 5_000;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [PerfFact]
    public async Task Import_of_100k_rows_meets_the_throughput_and_memory_baseline()
    {
        var csv = new StringBuilder("code,name,qty\n", Rows * 24);
        for (var i = 0; i < Rows; i++)
            csv.Append(CultureInfo.InvariantCulture, $"P{i:D7},Product {i},{i % 1000}\n");
        var job = await fx.StartImportAsync(A.TenantA, csv.ToString());

        var result = await MeasureAsync(() => fx.RunImportAsync(A.TenantA, job.Id));

        var done = await fx.JobAsync(A.TenantA, job.Id);
        done.Should().BeEquivalentTo(new { Status = TransferJobStatus.Completed, CreatedRows = (long)Rows });
        await ReportAsync("import_csv_100k", result);
    }

    [PerfFact]
    public async Task Export_of_100k_rows_to_xlsx_meets_the_throughput_and_memory_baseline()
    {
        for (var i = 0; i < Rows; i++)
            fx.Items.Rows[(A.TenantA.Id, $"P{i:D7}")] = ($"Product {i}", i % 1000);
        var job = await fx.SendAsync(A.TenantA, new StartExportCommand(TestItemImportDefinition.DefinitionKey, TransferFormat.Xlsx));

        var result = await MeasureAsync(() => fx.RunExportAsync(A.TenantA, job.Id));

        var done = await fx.JobAsync(A.TenantA, job.Id);
        done.Should().BeEquivalentTo(new { Status = TransferJobStatus.Completed, ProcessedRows = (long)Rows });
        await ReportAsync("export_xlsx_100k", result);
    }

    private sealed record Measurement(TimeSpan Elapsed, double PeakHeapGrowthMb)
    {
        public double RowsPerSecond => Rows / Elapsed.TotalSeconds;
    }

    /// <summary>Wall time, and the peak managed-heap growth sampled every 20 ms while the job runs.</summary>
    private static async Task<Measurement> MeasureAsync(Func<Task> run)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var baseline = GC.GetTotalMemory(forceFullCollection: true);
        var peak = baseline;
        using var stop = new CancellationTokenSource();
        var sampler = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                peak = Math.Max(peak, GC.GetTotalMemory(forceFullCollection: false));
                await Task.Delay(20).ConfigureAwait(false);
            }
        });

        var watch = Stopwatch.StartNew();
        await run();
        watch.Stop();
        await stop.CancelAsync();
        await sampler;
        return new Measurement(watch.Elapsed, (peak - baseline) / 1024d / 1024d);
    }

    private async Task ReportAsync(string scenario, Measurement result)
    {
        output.WriteLine(
            $"{scenario}: {Rows} rows in {result.Elapsed.TotalSeconds:F2} s = {result.RowsPerSecond:F0} rows/s, " +
            $"peak heap growth {result.PeakHeapGrowthMb:F1} MB (thresholds: >= {MinRowsPerSecond} rows/s, <= {MaxHeapGrowthMb} MB)");

        var directory = Path.Combine(AppContext.BaseDirectory, "TestResults");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "datatransfer-perf.json");
        var results = File.Exists(file)
            ? JsonSerializer.Deserialize<Dictionary<string, object>>(await File.ReadAllTextAsync(file)) ?? []
            : [];
        results[scenario] = new
        {
            rows = Rows,
            seconds = Math.Round(result.Elapsed.TotalSeconds, 3),
            rowsPerSecond = Math.Round(result.RowsPerSecond),
            peakHeapGrowthMb = Math.Round(result.PeakHeapGrowthMb, 1),
            measuredAt = DateTime.UtcNow
        };
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));

        result.RowsPerSecond.Should().BeGreaterThanOrEqualTo(MinRowsPerSecond, "the throughput baseline");
        result.PeakHeapGrowthMb.Should().BeLessThanOrEqualTo(MaxHeapGrowthMb, "streaming keeps memory flat");
    }

    private static double Threshold(string variable, double fallback) =>
        double.TryParse(Environment.GetEnvironmentVariable(variable), NumberStyles.Float, CultureInfo.InvariantCulture,
            out var value) ? value : fallback;
}
