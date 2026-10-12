using Accounting.Domain;

namespace Accounting.UnitTests;

public sealed class EstimateExportTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void New_export_is_pending_with_one_attempt()
    {
        EstimateExport export = EstimateExport.Start("tenant-a", Guid.NewGuid(), AccountingProvider.Mock, "AUD", 100m, Now);

        Assert.Equal(ExportStatus.Pending, export.Status);
        Assert.Equal(1, export.AttemptCount);
    }

    [Fact]
    public void Succeeded_export_cannot_be_retried_or_failed()
    {
        EstimateExport export = EstimateExport.Start("tenant-a", Guid.NewGuid(), AccountingProvider.Mock, "AUD", 100m, Now);
        export.MarkSucceeded("REF-1", Now);

        Assert.Throws<InvalidOperationException>(() => export.Retry("AUD", 100m, Now));
        Assert.Throws<InvalidOperationException>(() => export.MarkFailed("X", "y", Now));
        Assert.Equal("REF-1", export.ExternalReference);
    }

    [Fact]
    public void Failure_message_is_truncated_to_the_column_size()
    {
        EstimateExport export = EstimateExport.Start("tenant-a", Guid.NewGuid(), AccountingProvider.Mock, "AUD", 100m, Now);

        export.MarkFailed("CODE", new string('x', 2_000), Now);

        Assert.Equal(EstimateExport.MaxErrorMessageLength, export.LastErrorMessage!.Length);
    }

    [Fact]
    public void Pending_export_becomes_stale_after_the_timeout()
    {
        EstimateExport export = EstimateExport.Start("tenant-a", Guid.NewGuid(), AccountingProvider.Mock, "AUD", 100m, Now);

        Assert.False(export.IsStalePending(Now.AddSeconds(30), TimeSpan.FromSeconds(60)));
        Assert.True(export.IsStalePending(Now.AddSeconds(61), TimeSpan.FromSeconds(60)));
    }
}
