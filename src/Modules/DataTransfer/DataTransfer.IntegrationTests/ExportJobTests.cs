using DataTransfer.Engine;
using DataTransfer.Options;
using A = DataTransfer.IntegrationTests.DataTransferPostgresFixture;

namespace DataTransfer.IntegrationTests;

[Collection(DataTransferPostgresCollection.Name)]
public sealed class ExportJobTests(DataTransferPostgresFixture fx) : IAsyncLifetime
{
    public async ValueTask InitializeAsync() => await fx.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [InlineData(TransferFormat.Xlsx)]
    [InlineData(TransferFormat.Csv)]
    public async Task Export_writes_every_row_of_the_tenant_into_a_private_job_file(TransferFormat format)
    {
        Seed(A.TenantA.Id, ("A", "Apple", 1), ("B", "بنانا", 2), ("C", "=cmd()", 3));
        Seed(A.TenantB.Id, ("Z", "Other tenant", 9));

        var job = await fx.SendAsync(A.TenantA, new StartExportCommand(TestItemImportDefinition.DefinitionKey, format));
        await fx.RunExportAsync(A.TenantA, job.Id);

        var done = await fx.JobAsync(A.TenantA, job.Id);
        done.Should().BeEquivalentTo(new { Status = TransferJobStatus.Completed, ProcessedRows = 3L, TotalRows = (long?)3 });
        var rows = await ReadAsync(fx.Attachments.Files[done.ResultAttachmentId!.Value].Content, format);
        rows[0].Should().Equal("Code", "Name", "Quantity");
        rows.Skip(1).Select(r => CellText.Clean(r[1])).Should().Equal("Apple", "بنانا", "=cmd()");

        var file = await fx.SendAsync(A.TenantA, new GetTransferJobFileQuery(job.Id, TransferJobFileKind.Result));
        file.FileName.Should().EndWith(TabularWriter.Extension(format));
    }

    [Fact]
    public async Task Export_applies_the_definition_filter()
    {
        Seed(A.TenantA.Id, ("A", "Apple", 1), ("B", "Banana", 5));

        var job = await fx.SendAsync(A.TenantA, new StartExportCommand(TestItemImportDefinition.DefinitionKey,
            TransferFormat.Csv, new Dictionary<string, string> { ["minQty"] = "3" }));
        await fx.RunExportAsync(A.TenantA, job.Id);

        (await fx.JobAsync(A.TenantA, job.Id)).ProcessedRows.Should().Be(1);
    }

    [Fact]
    public async Task Cancelling_a_running_export_stops_at_the_next_progress_point_without_a_result_file()
    {
        Seed(A.TenantA.Id, ("A", "1", 1), ("B", "2", 2), ("C", "3", 3), ("D", "4", 4), ("E", "5", 5));
        var job = await fx.SendAsync(A.TenantA, new StartExportCommand(TestItemImportDefinition.DefinitionKey, TransferFormat.Csv));
        fx.Items.AfterBatch = () => fx.SendAsync(A.TenantA, new CancelTransferJobCommand(job.Id)); // after the first row

        await fx.RunExportAsync(A.TenantA, job.Id);

        var done = await fx.JobAsync(A.TenantA, job.Id);
        done.Status.Should().Be(TransferJobStatus.Cancelled);
        done.ProcessedRows.Should().Be(2, "progress (and the cancel check) runs every 2 rows");
        done.ResultAttachmentId.Should().BeNull();
        fx.Attachments.Of(job.Id).Should().BeEmpty();
    }

    [Fact]
    public async Task A_transient_upload_failure_is_retried_then_succeeds()
    {
        Seed(A.TenantA.Id, ("A", "Apple", 1));
        fx.Attachments.RejectUploads = new CustomException("Scanner down", System.Net.HttpStatusCode.ServiceUnavailable)
        {
            ErrorCode = "malware_scanner_unavailable"
        };
        var job = await fx.SendAsync(A.TenantA, new StartExportCommand(TestItemImportDefinition.DefinitionKey, TransferFormat.Csv));

        await fx.RunExportAsync(A.TenantA, job.Id);
        (await fx.JobAsync(A.TenantA, job.Id)).Status.Should().Be(TransferJobStatus.Running);
        fx.Jobs.For(job.Id).Should().Contain(j => j.IsScheduled);

        fx.Attachments.RejectUploads = null;
        await fx.RunExportAsync(A.TenantA, job.Id);

        (await fx.JobAsync(A.TenantA, job.Id)).Status.Should().Be(TransferJobStatus.Completed);
    }

    [Fact]
    public async Task A_permanent_upload_rejection_fails_at_once_with_its_code()
    {
        Seed(A.TenantA.Id, ("A", "Apple", 1));
        fx.Attachments.RejectUploads = new BadRequestException("Infected") { ErrorCode = "malware_detected" };
        var job = await fx.SendAsync(A.TenantA, new StartExportCommand(TestItemImportDefinition.DefinitionKey, TransferFormat.Csv));

        await fx.RunExportAsync(A.TenantA, job.Id);

        (await fx.JobAsync(A.TenantA, job.Id)).Should().BeEquivalentTo(new
        {
            Status = TransferJobStatus.Failed,
            FailureCode = "malware_detected",
            Attempts = 1
        });
        fx.Jobs.For(job.Id).Should().NotContain(j => j.IsScheduled);
    }

    [Fact]
    public async Task An_export_over_the_file_size_limit_fails_at_once_before_upload()
    {
        Seed(A.TenantA.Id, ("A", new string('x', 500), 1));
        fx.Options.Limits.MaxFileBytes = 100;
        var job = await fx.SendAsync(A.TenantA, new StartExportCommand(TestItemImportDefinition.DefinitionKey, TransferFormat.Csv));

        await fx.RunExportAsync(A.TenantA, job.Id);

        (await fx.JobAsync(A.TenantA, job.Id)).Should().BeEquivalentTo(new
        {
            Status = TransferJobStatus.Failed,
            FailureCode = "file_too_large",
            Attempts = 1
        });
        fx.Attachments.Of(job.Id).Should().BeEmpty();
    }

    [Fact]
    public async Task An_export_over_the_row_limit_fails_with_too_many_rows()
    {
        Seed(A.TenantA.Id, ("A", "1", 1), ("B", "2", 2), ("C", "3", 3));
        fx.Options.Limits.MaxExportRows = 2;
        var job = await fx.SendAsync(A.TenantA, new StartExportCommand(TestItemImportDefinition.DefinitionKey, TransferFormat.Csv));

        await fx.RunExportAsync(A.TenantA, job.Id);

        (await fx.JobAsync(A.TenantA, job.Id)).FailureCode.Should().Be("too_many_rows");
    }

    [Fact]
    public async Task An_auditor_needs_the_definition_permission_to_download_someone_elses_files()
    {
        Seed(A.TenantA.Id, ("A", "Apple", 1));
        var job = await fx.SendAsync(A.TenantA, new StartExportCommand(TestItemImportDefinition.DefinitionKey, TransferFormat.Csv));
        await fx.RunExportAsync(A.TenantA, job.Id);
        var auditor = Guid.NewGuid();

        fx.Caller.Denied.Add(Shared.Core.MultiTenancy.Authorization.TenantPermissions.ProductsManage);
        var withoutPermission = () => fx.SendAsync(A.TenantA, new GetTransferJobFileQuery(job.Id, TransferJobFileKind.Result), auditor);
        await withoutPermission.Should().ThrowAsync<ForbiddenException>();
        (await fx.SendAsync(A.TenantA, new GetTransferJobByIdQuery(job.Id), auditor)).Job.Id.Should().Be(job.Id,
            "the job's metadata stays visible to the auditor");
        (await fx.SendAsync(A.TenantA, new GetTransferJobFileQuery(job.Id, TransferJobFileKind.Result)))
            .FileName.Should().EndWith(".csv", "the requester keeps access to their own files");

        fx.Caller.Denied.Clear();
        (await fx.SendAsync(A.TenantA, new GetTransferJobFileQuery(job.Id, TransferJobFileKind.Result), auditor))
            .FileName.Should().EndWith(".csv");
    }

    [Fact]
    public async Task Template_has_localized_headers_with_required_markers_and_a_sample_row()
    {
        var template = await fx.SendAsync(A.TenantA,
            new GetTransferTemplateQuery(TestItemImportDefinition.DefinitionKey, TransferFormat.Xlsx));

        var rows = await ReadAsync(template.Content, TransferFormat.Xlsx);
        rows[0].Should().Equal("Code *", "Name *", "Quantity");
        rows[1].Should().Equal("A-1", "Apple", "3");
    }

    [Fact]
    public async Task Definitions_list_only_what_the_caller_may_use()
    {
        (await fx.SendAsync(A.TenantA, new GetTransferDefinitionsQuery()))
            .Should().ContainSingle(d => d.Key == TestItemImportDefinition.DefinitionKey && d.CanImport && d.CanExport);

        fx.Caller.Denied.Add(Shared.Core.MultiTenancy.Authorization.TenantPermissions.DataImport);
        (await fx.SendAsync(A.TenantA, new GetTransferDefinitionsQuery()))
            .Should().ContainSingle(d => d.Key == TestItemImportDefinition.DefinitionKey && !d.CanImport && d.CanExport);

        fx.Caller.Denied.Add(Shared.Core.MultiTenancy.Authorization.TenantPermissions.ProductsManage);
        (await fx.SendAsync(A.TenantA, new GetTransferDefinitionsQuery())).Should().BeEmpty();
    }

    private void Seed(Guid tenant, params (string Code, string Name, int Qty)[] rows)
    {
        foreach (var (code, name, qty) in rows)
            fx.Items.Rows[(tenant, code)] = (name, qty);
    }

    private static async Task<List<string?[]>> ReadAsync(byte[] content, TransferFormat format)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dt-it-{Guid.NewGuid():N}{TabularWriter.Extension(format)}");
        await File.WriteAllBytesAsync(path, content);
        try
        {
            await using var reader = await TabularReader.OpenAsync(path, format, new DataTransferLimits(), CancellationToken.None);
            var rows = new List<string?[]> { reader.Header.ToArray() };
            await foreach (var row in reader.ReadRowsAsync(0, CancellationToken.None))
                rows.Add(row.Cells);
            return rows;
        }
        finally
        {
            File.Delete(path);
        }
    }
}
