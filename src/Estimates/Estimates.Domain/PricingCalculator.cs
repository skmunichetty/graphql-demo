namespace Estimates.Domain;

/// <summary>The calculated money amounts for one line item.</summary>
public sealed record LineAmounts(decimal Subtotal, decimal TaxAmount, decimal Total);

/// <summary>
/// Pricing and rounding rules. All money uses <see cref="decimal"/>, never double,
/// because binary floating point cannot represent values such as 0.1 exactly.
///
/// Rules (documented for clients in docs/pricing-rules.md):
/// 1. Line subtotal = quantity x unit price, rounded to 2 decimal places.
/// 2. Line tax      = line subtotal x (tax rate percent / 100), rounded to 2 decimal places.
/// 3. Line total    = line subtotal + line tax.
/// 4. Estimate subtotal, tax total and total are the sums of the already-rounded line values,
///    so the displayed lines always add up exactly to the displayed totals.
/// 5. Rounding uses MidpointRounding.AwayFromZero (2.345 -> 2.35), the "commercial" rounding
///    most people expect. .NET's default is banker's rounding (2.345 -> 2.34), so it is explicit here.
///
/// Tax is DEMO behaviour: a per-line percentage. Real tax rules (jurisdictions, inclusive pricing,
/// invoice-level rounding, exemptions) are out of scope and are not claimed to be compliant.
/// </summary>
public static class PricingCalculator
{
    /// <summary>All supported currencies use two minor-unit digits (cents).</summary>
    public const int MoneyDecimalPlaces = 2;

    public static LineAmounts CalculateLine(decimal quantity, decimal unitPrice, decimal taxRatePercent)
    {
        decimal subtotal = RoundMoney(quantity * unitPrice);
        decimal taxAmount = RoundMoney(subtotal * taxRatePercent / 100m);
        decimal total = subtotal + taxAmount;
        return new LineAmounts(subtotal, taxAmount, total);
    }

    public static decimal RoundMoney(decimal amount)
    {
        return Math.Round(amount, MoneyDecimalPlaces, MidpointRounding.AwayFromZero);
    }
}
