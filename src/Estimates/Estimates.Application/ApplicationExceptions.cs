using Estimates.Domain;

namespace Estimates.Application;

/// <summary>
/// The estimate does not exist for the caller's tenant. Estimates owned by another tenant produce
/// exactly the same error, so a caller cannot discover which IDs exist in other tenants.
/// </summary>
public sealed class EstimateNotFoundException : DomainException
{
    public EstimateNotFoundException(Guid estimateId)
        : base(ErrorCodes.EstimateNotFound, $"Estimate '{estimateId}' was not found.")
    {
        EstimateId = estimateId;
    }

    public Guid EstimateId { get; }
}

/// <summary>
/// The estimate changed after the client last read it (optimistic concurrency).
/// The client should re-query the estimate, re-apply its change and retry with the new version.
/// </summary>
public sealed class ConcurrencyConflictException : DomainException
{
    public ConcurrencyConflictException(
        Guid estimateId,
        int expectedVersion,
        int? currentVersion,
        Exception? innerException = null)
        : base(
            ErrorCodes.ConcurrencyConflict,
            "The estimate was changed by another request. Reload it and try again with the latest version.",
            innerException)
    {
        EstimateId = estimateId;
        ExpectedVersion = expectedVersion;
        CurrentVersion = currentVersion;
    }

    public Guid EstimateId { get; }

    public int ExpectedVersion { get; }

    /// <summary>The version currently stored, when known.</summary>
    public int? CurrentVersion { get; }
}
