using System.ComponentModel.DataAnnotations;

namespace Estimates.Application;

/// <summary>
/// Configurable DEMO tax behaviour (section "Pricing" in appsettings.json).
/// This is not tax advice or a compliant tax engine; it only supplies a default rate.
/// </summary>
public sealed class PricingOptions
{
    public const string SectionName = "Pricing";

    /// <summary>Used when a line item is added without an explicit tax rate. Example: 10 means 10%.</summary>
    [Range(0, 100)]
    public decimal DefaultTaxRatePercent { get; set; } = 10m;
}
