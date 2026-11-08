using Xunit;

// The "E2E" and multi-tenant collections each boot their own host against their own Postgres container, but Hangfire's
// storage is process-global (JobStorage.Current / GlobalConfiguration, last host started wins, also used by the static
// RecurringJob.AddOrUpdate bootstrap calls). Run in parallel, a test could therefore enqueue against another fixture's
// already-disposed container: intermittent 500s / "Failed to connect". Collections run one after the other instead;
// tests inside a collection were already sequential, so only the cross-collection overlap is lost.
// The switch is "parallelizeTestCollections": false in xunit.runner.json (xUnit v3 obsoletes the assembly attribute).
