using Estimates.Domain;

namespace Estimates.UnitTests;

public sealed class PricingCalculatorTests
{
    [Fact]
    public void CalculateLine_multiplies_quantity_by_unit_price_and_adds_tax()
    {
        LineAmounts amounts = PricingCalculator.CalculateLine(quantity: 3m, unitPrice: 12.50m, taxRatePercent: 10m);

        Assert.Equal(37.50m, amounts.Subtotal);
        Assert.Equal(3.75m, amounts.TaxAmount);
        Assert.Equal(41.25m, amounts.Total);
    }

    [Fact]
    public void CalculateLine_rounds_subtotal_half_away_from_zero()
    {
        // 2.5 x 0.333 = 0.8325 -> 0.83 ; 1.5 x 1.23 = 1.845 -> 1.85 (banker's rounding would give 1.84)
        LineAmounts first = PricingCalculator.CalculateLine(2.5m, 0.333m, 0m);
        LineAmounts second = PricingCalculator.CalculateLine(1.5m, 1.23m, 0m);

        Assert.Equal(0.83m, first.Subtotal);
        Assert.Equal(1.85m, second.Subtotal);
    }

    [Fact]
    public void CalculateLine_rounds_tax_after_rounding_subtotal()
    {
        // Subtotal 10.05 ; tax 15% = 1.5075 -> 1.51
        LineAmounts amounts = PricingCalculator.CalculateLine(1m, 10.05m, 15m);

        Assert.Equal(10.05m, amounts.Subtotal);
        Assert.Equal(1.51m, amounts.TaxAmount);
        Assert.Equal(11.56m, amounts.Total);
    }

    [Fact]
    public void CalculateLine_with_zero_tax_has_total_equal_to_subtotal()
    {
        LineAmounts amounts = PricingCalculator.CalculateLine(4m, 25m, 0m);

        Assert.Equal(100m, amounts.Subtotal);
        Assert.Equal(0m, amounts.TaxAmount);
        Assert.Equal(100m, amounts.Total);
    }

    [Theory]
    [InlineData("0.005", "0.01")]
    [InlineData("0.004", "0.00")]
    [InlineData("2.345", "2.35")]
    [InlineData("-2.345", "-2.35")]
    public void RoundMoney_uses_midpoint_away_from_zero(string input, string expected)
    {
        decimal result = PricingCalculator.RoundMoney(decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), result);
    }
}
