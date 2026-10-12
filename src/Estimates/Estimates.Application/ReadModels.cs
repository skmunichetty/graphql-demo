using Estimates.Domain;

namespace Estimates.Application;

/// <summary>
/// Read model returned to the API. It is deliberately separate from the <see cref="Estimate"/>
/// aggregate so that the GraphQL schema can evolve independently of the domain model, and so
/// that list queries can be projected directly in SQL without loading whole aggregates.
/// These are classes with init properties (not positional records) because EF Core needs
/// member-initialisation to translate ordering and cursor paging over a projection.
/// </summary>
public sealed class EstimateReadModel
{
    public required Guid Id { get; init; }

    public required string TenantId { get; init; }

    public required CustomerReadModel Customer { get; init; }

    public required string CurrencyCode { get; init; }

    public required EstimateStatus Status { get; init; }

    public required decimal Subtotal { get; init; }

    public required decimal TaxTotal { get; init; }

    public required decimal Total { get; init; }

    public required int Version { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public DateTimeOffset? ApprovedAt { get; init; }

    public static EstimateReadModel FromEstimate(Estimate estimate)
    {
        ArgumentNullException.ThrowIfNull(estimate);
        return new EstimateReadModel
        {
            Id = estimate.Id,
            TenantId = estimate.TenantId,
            Customer = new CustomerReadModel
            {
                Name = estimate.Customer.Name,
                Email = estimate.Customer.Email,
                Phone = estimate.Customer.Phone,
            },
            CurrencyCode = estimate.CurrencyCode,
            Status = estimate.Status,
            Subtotal = estimate.Subtotal,
            TaxTotal = estimate.TaxTotal,
            Total = estimate.Total,
            Version = estimate.Version,
            CreatedAt = estimate.CreatedAt,
            UpdatedAt = estimate.UpdatedAt,
            ApprovedAt = estimate.ApprovedAt,
        };
    }
}

public sealed class CustomerReadModel
{
    public required string Name { get; init; }

    public string? Email { get; init; }

    public string? Phone { get; init; }
}

public sealed class LineItemReadModel
{
    public required Guid Id { get; init; }

    public required Guid EstimateId { get; init; }

    public required int Position { get; init; }

    public required string Description { get; init; }

    public required decimal Quantity { get; init; }

    public required decimal UnitPrice { get; init; }

    public required decimal TaxRatePercent { get; init; }

    public required decimal Subtotal { get; init; }

    public required decimal TaxAmount { get; init; }

    public required decimal Total { get; init; }
}
