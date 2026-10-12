using Accounting.Domain;

namespace Accounting.Application;

/// <summary>Read model returned to the API. A class with init properties so EF Core can project and page it.</summary>
public sealed class EstimateExportReadModel
{
    public required Guid Id { get; init; }

    public required Guid EstimateId { get; init; }

    public required AccountingProvider Provider { get; init; }

    public required ExportStatus Status { get; init; }

    public string? ExternalReference { get; init; }

    public required string CurrencyCode { get; init; }

    public required decimal ExportedTotal { get; init; }

    public required int AttemptCount { get; init; }

    public string? LastErrorCode { get; init; }

    public string? LastErrorMessage { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public static EstimateExportReadModel FromExport(EstimateExport export)
    {
        ArgumentNullException.ThrowIfNull(export);
        return new EstimateExportReadModel
        {
            Id = export.Id,
            EstimateId = export.EstimateId,
            Provider = export.Provider,
            Status = export.Status,
            ExternalReference = export.ExternalReference,
            CurrencyCode = export.CurrencyCode,
            ExportedTotal = export.ExportedTotal,
            AttemptCount = export.AttemptCount,
            LastErrorCode = export.LastErrorCode,
            LastErrorMessage = export.LastErrorMessage,
            CreatedAt = export.CreatedAt,
            UpdatedAt = export.UpdatedAt,
            CompletedAt = export.CompletedAt,
        };
    }
}
