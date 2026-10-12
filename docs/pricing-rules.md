# Pricing, tax and rounding rules

Implemented in `src/Estimates/Estimates.Domain/PricingCalculator.cs` and tested in
`tests/Estimates.UnitTests/PricingCalculatorTests.cs` and `EstimateTests.cs`.

> **Demo behaviour.** Tax here is a simple per-line percentage, used for demonstration. It is not tax
> advice and makes no claim of compliance with any jurisdiction's tax rules (GST/VAT inclusive
> pricing, invoice-level rounding, exemptions, reverse charge, and so on).

## Money

- All amounts are `decimal` in C# and `numeric` in PostgreSQL. Floating point is never used, because
  `double` cannot represent values like 0.1 exactly.
- Each estimate has one currency: `AUD`, `CAD`, `EUR`, `GBP`, `NZD` or `USD`. There is no currency
  conversion. All supported currencies have 2 minor-unit digits, and the rounding rules depend on
  that. Adding currencies such as JPY (0 digits) or KWD (3 digits) needs a rule change first.
- In GraphQL, amounts use Hot Chocolate's `Decimal` scalar, which is serialised as a JSON number.
  JavaScript clients should use a decimal library for arithmetic rather than JavaScript `number`.

## Calculation

For each line:

1. `subtotal = round(quantity × unitPrice, 2)`
2. `taxAmount = round(subtotal × taxRatePercent / 100, 2)`
3. `total = subtotal + taxAmount`

For the estimate:

4. `subtotal = Σ line subtotals`, `taxTotal = Σ line taxAmounts`, `total = subtotal + taxTotal`

Rounding is **half away from zero** (`MidpointRounding.AwayFromZero`): 2.345 → 2.35. This is written
explicitly because .NET's default is banker's rounding (2.345 → 2.34).

Tax is rounded **per line**, and the estimate totals add up the rounded lines. Displayed lines
therefore always add up exactly to the displayed totals. The trade-off: the tax total can differ by a
few cents from rounding the tax on the whole subtotal once. Example: three lines of 1 × 0.10 at 15%
tax give 3 × 0.02 = 0.06, not round(0.045) = 0.05. Some jurisdictions require invoice-level
rounding; that would be a rule change, not a client concern.

## Input validation

| Field | Rule |
|---|---|
| `quantity` | > 0, ≤ 1,000,000, at most 4 decimal places |
| `unitPrice` | ≥ 0 (free items are allowed), ≤ 10,000,000, at most 4 decimal places |
| `taxRatePercent` | 0–100, at most 4 decimal places; optional on `addLineItem` (default from `Pricing:DefaultTaxRatePercent`, 10) |
| `description` | Required, ≤ 500 characters |
| Line items per estimate | ≤ 200 |
| `customerName` | Required, ≤ 200 characters; `customerEmail` optional, must be a valid address |

With these limits, the largest possible estimate total is 4 × 10¹⁵, which is inside the
`numeric(18,2)` columns. The database also has check constraints (quantity > 0, unit price ≥ 0,
tax rate 0–100, `total = subtotal + tax_amount`) as a second line of defence.

## Configuration

```json
"Pricing": { "DefaultTaxRatePercent": 10 }
```

This value is validated at startup (0–100). It applies only when a client leaves `taxRatePercent` out.
