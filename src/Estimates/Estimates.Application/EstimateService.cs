using Estimates.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Estimates.Application;

/// <summary>
/// Use cases that change estimates. Each method follows the same shape:
/// load the aggregate for the tenant, check the expected version, call the domain, save.
/// Business rules live in the domain; this class only coordinates.
/// </summary>
public sealed class EstimateService
{
    private readonly IEstimateRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly PricingOptions _pricingOptions;
    private readonly ILogger<EstimateService> _logger;

    public EstimateService(
        IEstimateRepository repository,
        TimeProvider timeProvider,
        IOptions<PricingOptions> pricingOptions,
        ILogger<EstimateService> logger)
    {
        _repository = repository;
        _timeProvider = timeProvider;
        _pricingOptions = pricingOptions.Value;
        _logger = logger;
    }

    public async Task<EstimateReadModel> CreateDraftAsync(
        string tenantId,
        CreateEstimateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        CustomerDetails customer = CustomerDetails.Create(command.CustomerName, command.CustomerEmail, command.CustomerPhone);
        Estimate estimate = Estimate.CreateDraft(tenantId, customer, command.CurrencyCode, _timeProvider.GetUtcNow());

        _repository.Add(estimate);
        await _repository.SaveChangesAsync(estimate, cancellationToken);

        _logger.LogInformation(
            "Created draft estimate {EstimateId} for tenant {TenantId} in {CurrencyCode}",
            estimate.Id,
            tenantId,
            estimate.CurrencyCode);

        return EstimateReadModel.FromEstimate(estimate);
    }

    public async Task<EstimateReadModel> AddLineItemAsync(
        string tenantId,
        AddLineItemCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Estimate estimate = await LoadForUpdateAsync(tenantId, command.EstimateId, command.ExpectedVersion, cancellationToken);
        decimal taxRatePercent = command.TaxRatePercent ?? _pricingOptions.DefaultTaxRatePercent;

        estimate.AddLineItem(
            command.Description,
            command.Quantity,
            command.UnitPrice,
            taxRatePercent,
            _timeProvider.GetUtcNow());

        await _repository.SaveChangesAsync(estimate, cancellationToken);
        return EstimateReadModel.FromEstimate(estimate);
    }

    public async Task<EstimateReadModel> UpdateLineItemAsync(
        string tenantId,
        UpdateLineItemCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Estimate estimate = await LoadForUpdateAsync(tenantId, command.EstimateId, command.ExpectedVersion, cancellationToken);

        estimate.UpdateLineItem(
            command.LineItemId,
            command.Description,
            command.Quantity,
            command.UnitPrice,
            command.TaxRatePercent,
            _timeProvider.GetUtcNow());

        await _repository.SaveChangesAsync(estimate, cancellationToken);
        return EstimateReadModel.FromEstimate(estimate);
    }

    public async Task<EstimateReadModel> RemoveLineItemAsync(
        string tenantId,
        RemoveLineItemCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Estimate estimate = await LoadForUpdateAsync(tenantId, command.EstimateId, command.ExpectedVersion, cancellationToken);
        estimate.RemoveLineItem(command.LineItemId, _timeProvider.GetUtcNow());

        await _repository.SaveChangesAsync(estimate, cancellationToken);
        return EstimateReadModel.FromEstimate(estimate);
    }

    public async Task<EstimateReadModel> ApproveAsync(
        string tenantId,
        ApproveEstimateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Estimate estimate = await LoadForUpdateAsync(tenantId, command.EstimateId, command.ExpectedVersion, cancellationToken);
        estimate.Approve(_timeProvider.GetUtcNow());

        await _repository.SaveChangesAsync(estimate, cancellationToken);

        _logger.LogInformation(
            "Approved estimate {EstimateId} for tenant {TenantId} with total {Total} {CurrencyCode}",
            estimate.Id,
            tenantId,
            estimate.Total,
            estimate.CurrencyCode);

        return EstimateReadModel.FromEstimate(estimate);
    }

    private async Task<Estimate> LoadForUpdateAsync(
        string tenantId,
        Guid estimateId,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        Estimate? estimate = await _repository.GetForUpdateAsync(tenantId, estimateId, cancellationToken);
        if (estimate is null)
        {
            throw new EstimateNotFoundException(estimateId);
        }

        // First concurrency check: the client tells us which version it based its change on.
        // The database performs a second check on save to catch races between requests.
        if (estimate.Version != expectedVersion)
        {
            throw new ConcurrencyConflictException(estimateId, expectedVersion, estimate.Version);
        }

        return estimate;
    }
}
