namespace Search.IntegrationTests.Conformance;

/// <summary>The PostgreSQL engine, through the real module and migration, connected as a role that RLS applies to.</summary>
[Collection(SearchPostgresCollection.Name)]
public sealed class PostgresSearchEngineConformanceTests(SearchPostgresFixture fixture) : SearchEngineConformanceTests(fixture);
