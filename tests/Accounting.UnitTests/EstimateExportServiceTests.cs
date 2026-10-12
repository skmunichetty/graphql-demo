using Accounting.Application;
using Accounting.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Accounting.UnitTests;

public sealed class EstimateExportServiceTests
{
    private const string TenantId = "tenant-a";

    private readonly FakeEstimatesServiceClient _estimates = new();
    private readonly ScriptedAccountingProvider _provider = new();
    private readonly InMemoryExportRepository _repository = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly ExportOptions _options = new() { ProviderTimeoutSeconds = 1, PendingTimeoutSeconds = 60 };
    private readonly EstimateExportService _service;

    public EstimateExportServiceTests()
    {
        _service = new EstimateExportService(
            _repository,
            _estimates,
            new AccountingProviderRegistry([_provider]),
            _time,
            Options.Create(_options),
            NullLogger<EstimateExportService>.Instance);
    }

    private Guid AddApprovedEstimate(string customerName = "Acme Builders")
    {
        Guid estimateId = Guid.NewGuid();
        _estimates.Estimates[(TenantId, estimateId)] = TestData.Estimate(estimateId, customerName: customerName);
        return estimateId;
    }

    private Task<EstimateExportReadModel> ExportAsync(Guid estimateId)
    {
        return _service.ExportAsync(TenantId, estimateId, AccountingProvider.Mock, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Exports_approved_estimate_and_records_reference()
    {
        Guid estimateId = AddApprovedEstimate();
        _provider.Responses.Enqueue(() => "MOCK-INV-1");

        EstimateExportReadModel export = await ExportAsync(estimateId);

        Assert.Equal(ExportStatus.Succeeded, export.Status);
        Assert.Equal("MOCK-INV-1", export.ExternalReference);
        Assert.Equal(500.50m, export.ExportedTotal);
        Assert.Equal(1, export.AttemptCount);
        Assert.Equal(export.Id.ToString(), Assert.Single(_provider.IdempotencyKeys));
    }

    [Fact]
    public async Task Repeated_export_returns_existing_export_without_calling_provider_again()
    {
        Guid estimateId = AddApprovedEstimate();
        _provider.Responses.Enqueue(() => "MOCK-INV-1");

        EstimateExportReadModel first = await ExportAsync(estimateId);
        EstimateExportReadModel second = await ExportAsync(estimateId);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("MOCK-INV-1", second.ExternalReference);
        Assert.Single(_provider.IdempotencyKeys);
        Assert.Single(_repository.Exports);

        // A succeeded export does not even need to re-read the estimate.
        Assert.Equal(1, _estimates.CallCount);
    }

    [Theory]
    [InlineData("DRAFT")]
    [InlineData("SOME_FUTURE_STATUS")]
    public async Task Unapproved_estimate_is_rejected_and_nothing_is_recorded(string status)
    {
        Guid estimateId = Guid.NewGuid();
        _estimates.Estimates[(TenantId, estimateId)] = TestData.Estimate(estimateId, status: status);

        var exception = await Assert.ThrowsAsync<EstimateNotApprovedException>(() => ExportAsync(estimateId));

        Assert.Equal(ErrorCodes.EstimateNotApproved, exception.Code);
        Assert.Empty(_repository.Exports);
        Assert.Empty(_provider.IdempotencyKeys);
    }

    [Fact]
    public async Task Missing_estimate_is_rejected()
    {
        await Assert.ThrowsAsync<EstimateNotFoundException>(() => ExportAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Estimate_of_another_tenant_is_not_found()
    {
        Guid estimateId = Guid.NewGuid();
        _estimates.Estimates[("tenant-b", estimateId)] = TestData.Estimate(estimateId);

        await Assert.ThrowsAsync<EstimateNotFoundException>(() => ExportAsync(estimateId));
    }

    [Fact]
    public async Task Estimates_service_outage_is_reported_and_nothing_is_recorded()
    {
        Guid estimateId = AddApprovedEstimate();
        _estimates.IsUnavailable = true;

        var exception = await Assert.ThrowsAsync<EstimatesServiceUnavailableException>(() => ExportAsync(estimateId));

        Assert.Equal(ErrorCodes.EstimatesServiceUnavailable, exception.Code);
        Assert.Empty(_repository.Exports);
    }

    [Fact]
    public async Task Provider_failure_is_recorded_as_failed_export_and_retry_uses_same_record_and_key()
    {
        Guid estimateId = AddApprovedEstimate();
        _provider.Responses.Enqueue(() => throw new AccountingProviderException(ErrorCodes.ProviderUnavailable, "Provider is down."));
        _provider.Responses.Enqueue(() => "MOCK-INV-2");

        EstimateExportReadModel failed = await ExportAsync(estimateId);
        Assert.Equal(ExportStatus.Failed, failed.Status);
        Assert.Equal(ErrorCodes.ProviderUnavailable, failed.LastErrorCode);
        Assert.Null(failed.ExternalReference);

        EstimateExportReadModel retried = await ExportAsync(estimateId);
        Assert.Equal(failed.Id, retried.Id);
        Assert.Equal(ExportStatus.Succeeded, retried.Status);
        Assert.Equal(2, retried.AttemptCount);
        Assert.Null(retried.LastErrorCode);

        // Both attempts used the same idempotency key, so a provider that actually processed the
        // first attempt would not create a second invoice.
        Assert.Equal(2, _provider.IdempotencyKeys.Count);
        Assert.Single(_provider.IdempotencyKeys.Distinct());
    }

    [Fact]
    public async Task Provider_timeout_is_recorded_as_failed_export()
    {
        Guid estimateId = AddApprovedEstimate();
        _provider.Delay = TimeSpan.FromSeconds(30);

        // The provider timeout (1 second here) uses real time.
        EstimateExportReadModel export = await ExportAsync(estimateId);

        Assert.Equal(ExportStatus.Failed, export.Status);
        Assert.Equal(ErrorCodes.ProviderTimeout, export.LastErrorCode);
    }

    [Fact]
    public async Task Recent_pending_export_is_reported_as_in_progress()
    {
        Guid estimateId = AddApprovedEstimate();
        _repository.Exports.Add(EstimateExport.Start(TenantId, estimateId, AccountingProvider.Mock, "AUD", 500.50m, _time.GetUtcNow()));

        await Assert.ThrowsAsync<ExportInProgressException>(() => ExportAsync(estimateId));
        Assert.Empty(_provider.IdempotencyKeys);
    }

    [Fact]
    public async Task Stale_pending_export_is_retried()
    {
        Guid estimateId = AddApprovedEstimate();
        _repository.Exports.Add(EstimateExport.Start(TenantId, estimateId, AccountingProvider.Mock, "AUD", 500.50m, _time.GetUtcNow()));
        _time.Advance(TimeSpan.FromSeconds(_options.PendingTimeoutSeconds + 1));

        EstimateExportReadModel export = await ExportAsync(estimateId);

        Assert.Equal(ExportStatus.Succeeded, export.Status);
        Assert.Equal(2, export.AttemptCount);
    }

    [Fact]
    public async Task Unregistered_provider_is_rejected()
    {
        var serviceWithoutProviders = new EstimateExportService(
            _repository,
            _estimates,
            new AccountingProviderRegistry([]),
            _time,
            Options.Create(_options),
            NullLogger<EstimateExportService>.Instance);

        await Assert.ThrowsAsync<UnsupportedProviderException>(() =>
            serviceWithoutProviders.ExportAsync(TenantId, Guid.NewGuid(), AccountingProvider.Mock, TestContext.Current.CancellationToken));
    }
}
