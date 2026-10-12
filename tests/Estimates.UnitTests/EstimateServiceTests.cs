using Estimates.Application;
using Estimates.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Estimates.UnitTests;

public sealed class EstimateServiceTests
{
    private readonly InMemoryEstimateRepository _repository = new();
    private readonly EstimateService _service;

    public EstimateServiceTests()
    {
        var pricingOptions = Options.Create(new PricingOptions { DefaultTaxRatePercent = 15m });
        _service = new EstimateService(_repository, TimeProvider.System, pricingOptions, NullLogger<EstimateService>.Instance);
    }

    private Task<EstimateReadModel> CreateDraftAsync(string tenantId)
    {
        var command = new CreateEstimateCommand("Acme", null, null, "NZD");
        return _service.CreateDraftAsync(tenantId, command, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AddLineItem_uses_configured_default_tax_rate_when_none_is_given()
    {
        EstimateReadModel draft = await CreateDraftAsync("tenant-a");

        EstimateReadModel updated = await _service.AddLineItemAsync(
            "tenant-a",
            new AddLineItemCommand(draft.Id, draft.Version, "Tiles", 10m, 10m, TaxRatePercent: null),
            TestContext.Current.CancellationToken);

        Assert.Equal(15.00m, updated.TaxTotal);
        Assert.Equal(115.00m, updated.Total);
    }

    [Fact]
    public async Task Stale_expected_version_is_rejected_as_concurrency_conflict()
    {
        EstimateReadModel draft = await CreateDraftAsync("tenant-a");
        await _service.AddLineItemAsync(
            "tenant-a",
            new AddLineItemCommand(draft.Id, draft.Version, "First", 1m, 1m, 0m),
            TestContext.Current.CancellationToken);

        // Second client still holds version 1.
        var exception = await Assert.ThrowsAsync<ConcurrencyConflictException>(() => _service.AddLineItemAsync(
            "tenant-a",
            new AddLineItemCommand(draft.Id, draft.Version, "Second", 1m, 1m, 0m),
            TestContext.Current.CancellationToken));

        Assert.Equal(ErrorCodes.ConcurrencyConflict, exception.Code);
        Assert.Equal(2, exception.CurrentVersion);
    }

    [Fact]
    public async Task Estimate_from_another_tenant_is_reported_as_not_found()
    {
        EstimateReadModel draft = await CreateDraftAsync("tenant-a");

        var exception = await Assert.ThrowsAsync<EstimateNotFoundException>(() => _service.ApproveAsync(
            "tenant-b",
            new ApproveEstimateCommand(draft.Id, draft.Version),
            TestContext.Current.CancellationToken));

        Assert.Equal(ErrorCodes.EstimateNotFound, exception.Code);
    }

    /// <summary>A simple hand-written fake; no mocking library is needed for an interface this small.</summary>
    private sealed class InMemoryEstimateRepository : IEstimateRepository
    {
        private readonly List<Estimate> _estimates = [];

        public Task<Estimate?> GetForUpdateAsync(string tenantId, Guid estimateId, CancellationToken cancellationToken)
        {
            Estimate? estimate = _estimates.FirstOrDefault(item => item.Id == estimateId && item.TenantId == tenantId);
            return Task.FromResult(estimate);
        }

        public void Add(Estimate estimate)
        {
            _estimates.Add(estimate);
        }

        public Task SaveChangesAsync(Estimate estimate, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
