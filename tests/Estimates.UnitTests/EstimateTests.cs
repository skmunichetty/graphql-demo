using Estimates.Domain;

namespace Estimates.UnitTests;

public sealed class EstimateTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    private static Estimate CreateDraft()
    {
        CustomerDetails customer = CustomerDetails.Create("Acme Builders", "accounts@acme.example", null);
        return Estimate.CreateDraft("tenant-a", customer, "aud", Now);
    }

    [Fact]
    public void CreateDraft_starts_as_version_1_draft_with_zero_totals_and_normalised_currency()
    {
        Estimate estimate = CreateDraft();

        Assert.Equal(EstimateStatus.Draft, estimate.Status);
        Assert.Equal(1, estimate.Version);
        Assert.Equal("AUD", estimate.CurrencyCode);
        Assert.Equal(0m, estimate.Total);
        Assert.Empty(estimate.LineItems);
    }

    [Theory]
    [InlineData("")]
    [InlineData("XYZ")]
    [InlineData("JPY")]
    public void CreateDraft_rejects_unsupported_currency(string currencyCode)
    {
        CustomerDetails customer = CustomerDetails.Create("Acme", null, null);

        var exception = Assert.Throws<EstimateValidationException>(
            () => Estimate.CreateDraft("tenant-a", customer, currencyCode, Now));

        Assert.Equal("currencyCode", exception.Field);
        Assert.Equal(ErrorCodes.ValidationFailed, exception.Code);
    }

    [Theory]
    [InlineData("", "customerName")]
    [InlineData("   ", "customerName")]
    public void Customer_requires_a_name(string name, string expectedField)
    {
        var exception = Assert.Throws<EstimateValidationException>(() => CustomerDetails.Create(name, null, null));

        Assert.Equal(expectedField, exception.Field);
    }

    [Fact]
    public void Customer_rejects_invalid_email()
    {
        var exception = Assert.Throws<EstimateValidationException>(
            () => CustomerDetails.Create("Acme", "not-an-email", null));

        Assert.Equal("customerEmail", exception.Field);
    }

    [Fact]
    public void AddLineItem_updates_totals_and_version()
    {
        Estimate estimate = CreateDraft();

        estimate.AddLineItem("Timber framing", 10m, 45.50m, 10m, Now);
        estimate.AddLineItem("Labour", 8m, 95m, 10m, Now);

        Assert.Equal(1215.00m, estimate.Subtotal);
        Assert.Equal(121.50m, estimate.TaxTotal);
        Assert.Equal(1336.50m, estimate.Total);
        Assert.Equal(3, estimate.Version);
        Assert.Equal([1, 2], estimate.LineItems.Select(item => item.Position));
    }

    [Fact]
    public void Totals_are_sum_of_rounded_line_values()
    {
        Estimate estimate = CreateDraft();

        // Each line: 1 x 0.10 with 15% tax -> tax 0.015 -> 0.02 per line.
        // Three lines: tax total 0.06 (sum of rounded lines), not round(0.045) = 0.05.
        estimate.AddLineItem("Washer", 1m, 0.10m, 15m, Now);
        estimate.AddLineItem("Washer", 1m, 0.10m, 15m, Now);
        estimate.AddLineItem("Washer", 1m, 0.10m, 15m, Now);

        Assert.Equal(0.30m, estimate.Subtotal);
        Assert.Equal(0.06m, estimate.TaxTotal);
        Assert.Equal(0.36m, estimate.Total);
    }

    [Theory]
    [InlineData(0, "quantity")]
    [InlineData(-1, "quantity")]
    [InlineData(1_000_001, "quantity")]
    public void AddLineItem_rejects_invalid_quantity(decimal quantity, string expectedField)
    {
        Estimate estimate = CreateDraft();

        var exception = Assert.Throws<EstimateValidationException>(
            () => estimate.AddLineItem("Item", quantity, 10m, 10m, Now));

        Assert.Equal(expectedField, exception.Field);
        Assert.Equal(1, estimate.Version);
    }

    [Fact]
    public void AddLineItem_rejects_quantity_with_too_many_decimal_places()
    {
        Estimate estimate = CreateDraft();

        var exception = Assert.Throws<EstimateValidationException>(
            () => estimate.AddLineItem("Item", 1.23456m, 10m, 10m, Now));

        Assert.Equal("quantity", exception.Field);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(10_000_000.01)]
    public void AddLineItem_rejects_invalid_unit_price(decimal unitPrice)
    {
        Estimate estimate = CreateDraft();

        var exception = Assert.Throws<EstimateValidationException>(
            () => estimate.AddLineItem("Item", 1m, unitPrice, 10m, Now));

        Assert.Equal("unitPrice", exception.Field);
    }

    [Fact]
    public void AddLineItem_allows_zero_unit_price()
    {
        Estimate estimate = CreateDraft();

        estimate.AddLineItem("Site visit (no charge)", 1m, 0m, 10m, Now);

        Assert.Equal(0m, estimate.Total);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100.01)]
    public void AddLineItem_rejects_invalid_tax_rate(decimal taxRatePercent)
    {
        Estimate estimate = CreateDraft();

        var exception = Assert.Throws<EstimateValidationException>(
            () => estimate.AddLineItem("Item", 1m, 10m, taxRatePercent, Now));

        Assert.Equal("taxRatePercent", exception.Field);
    }

    [Fact]
    public void UpdateLineItem_recalculates_totals()
    {
        Estimate estimate = CreateDraft();
        LineItem lineItem = estimate.AddLineItem("Paint", 2m, 50m, 10m, Now);

        estimate.UpdateLineItem(lineItem.Id, "Paint (premium)", 3m, 60m, 10m, Now);

        Assert.Equal("Paint (premium)", estimate.LineItems.Single().Description);
        Assert.Equal(198.00m, estimate.Total);
    }

    [Fact]
    public void RemoveLineItem_recalculates_totals_and_renumbers_positions()
    {
        Estimate estimate = CreateDraft();
        LineItem first = estimate.AddLineItem("First", 1m, 10m, 0m, Now);
        estimate.AddLineItem("Second", 1m, 20m, 0m, Now);

        estimate.RemoveLineItem(first.Id, Now);

        LineItem remaining = Assert.Single(estimate.LineItems);
        Assert.Equal(1, remaining.Position);
        Assert.Equal(20m, estimate.Total);
    }

    [Fact]
    public void UpdateLineItem_with_unknown_id_throws_line_item_not_found()
    {
        Estimate estimate = CreateDraft();

        var exception = Assert.Throws<LineItemNotFoundException>(
            () => estimate.UpdateLineItem(Guid.NewGuid(), "x", 1m, 1m, 0m, Now));

        Assert.Equal(ErrorCodes.LineItemNotFound, exception.Code);
    }

    [Fact]
    public void Approve_requires_at_least_one_line_item()
    {
        Estimate estimate = CreateDraft();

        var exception = Assert.Throws<InvalidEstimateStateException>(() => estimate.Approve(Now));

        Assert.Equal(ErrorCodes.EstimateHasNoLineItems, exception.Code);
        Assert.Equal(EstimateStatus.Draft, estimate.Status);
    }

    [Fact]
    public void Approve_sets_status_and_timestamp()
    {
        Estimate estimate = CreateDraft();
        estimate.AddLineItem("Item", 1m, 10m, 10m, Now);

        estimate.Approve(Now.AddHours(1));

        Assert.Equal(EstimateStatus.Approved, estimate.Status);
        Assert.Equal(Now.AddHours(1), estimate.ApprovedAt);
    }

    [Fact]
    public void Approved_estimate_rejects_every_kind_of_edit()
    {
        Estimate estimate = CreateDraft();
        LineItem lineItem = estimate.AddLineItem("Item", 1m, 10m, 10m, Now);
        estimate.Approve(Now);
        int versionAfterApproval = estimate.Version;

        var add = Assert.Throws<InvalidEstimateStateException>(() => estimate.AddLineItem("New", 1m, 1m, 0m, Now));
        var update = Assert.Throws<InvalidEstimateStateException>(() => estimate.UpdateLineItem(lineItem.Id, "x", 1m, 1m, 0m, Now));
        var remove = Assert.Throws<InvalidEstimateStateException>(() => estimate.RemoveLineItem(lineItem.Id, Now));
        var approveAgain = Assert.Throws<InvalidEstimateStateException>(() => estimate.Approve(Now));

        Assert.All([add, update, remove, approveAgain], exception => Assert.Equal(ErrorCodes.EstimateNotEditable, exception.Code));
        Assert.Equal(versionAfterApproval, estimate.Version);
        Assert.Equal(11.00m, estimate.Total);
    }
}
