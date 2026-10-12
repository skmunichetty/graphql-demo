using System.Net.Mail;

namespace Estimates.Domain;

/// <summary>
/// Validation limits for estimate input. The limits are deliberately conservative: they keep
/// every possible total inside the numeric(18,2) database columns and stop absurd input early.
/// Maximum possible estimate total: 200 lines x 1,000,000 qty x 10,000,000 price x 2 (100% tax)
/// = 4 x 10^15, which is below the numeric(18,2) limit of 10^16.
/// </summary>
public static class EstimateRules
{
    public const int MaxCustomerNameLength = 200;
    public const int MaxCustomerEmailLength = 320;
    public const int MaxCustomerPhoneLength = 50;
    public const int MaxDescriptionLength = 500;
    public const int MaxLineItems = 200;

    public const decimal MaxQuantity = 1_000_000m;
    public const decimal MaxUnitPrice = 10_000_000m;
    public const decimal MaxTaxRatePercent = 100m;

    /// <summary>Quantities, unit prices and tax rates may use up to 4 decimal places, for example 0.0125 per screw.</summary>
    public const int MaxInputDecimalPlaces = 4;

    /// <summary>
    /// Supported ISO 4217 currency codes. All use 2 minor-unit digits, which the rounding rules rely on.
    /// Adding a currency with 0 or 3 minor units (JPY, KWD) requires changing the rounding rules first.
    /// </summary>
    public static readonly IReadOnlySet<string> SupportedCurrencyCodes =
        new HashSet<string>(StringComparer.Ordinal) { "AUD", "NZD", "USD", "GBP", "EUR", "CAD" };

    public static string ValidateCurrencyCode(string? currencyCode)
    {
        string normalized = (currencyCode ?? string.Empty).Trim().ToUpperInvariant();
        if (!SupportedCurrencyCodes.Contains(normalized))
        {
            string supported = string.Join(", ", SupportedCurrencyCodes.Order());
            throw new EstimateValidationException(
                "currencyCode",
                $"Currency code '{currencyCode}' is not supported. Supported codes: {supported}.");
        }

        return normalized;
    }

    public static string ValidateRequiredText(string? value, string field, int maxLength)
    {
        string trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new EstimateValidationException(field, $"'{field}' is required.");
        }

        if (trimmed.Length > maxLength)
        {
            throw new EstimateValidationException(field, $"'{field}' must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    public static string? ValidateOptionalText(string? value, string field, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new EstimateValidationException(field, $"'{field}' must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    public static string? ValidateOptionalEmail(string? email, string field)
    {
        string? trimmed = ValidateOptionalText(email, field, MaxCustomerEmailLength);
        if (trimmed is null)
        {
            return null;
        }

        bool isValid = MailAddress.TryCreate(trimmed, out MailAddress? parsed) && parsed.Address == trimmed;
        if (!isValid)
        {
            throw new EstimateValidationException(field, $"'{field}' is not a valid email address.");
        }

        return trimmed;
    }

    public static void ValidateQuantity(decimal quantity)
    {
        if (quantity <= 0m)
        {
            throw new EstimateValidationException("quantity", "Quantity must be greater than zero.");
        }

        if (quantity > MaxQuantity)
        {
            throw new EstimateValidationException("quantity", $"Quantity must not exceed {MaxQuantity}.");
        }

        ValidateDecimalPlaces(quantity, "quantity");
    }

    public static void ValidateUnitPrice(decimal unitPrice)
    {
        // Zero is allowed so that free items (for example "site visit - no charge") can be listed.
        if (unitPrice < 0m)
        {
            throw new EstimateValidationException("unitPrice", "Unit price must not be negative.");
        }

        if (unitPrice > MaxUnitPrice)
        {
            throw new EstimateValidationException("unitPrice", $"Unit price must not exceed {MaxUnitPrice}.");
        }

        ValidateDecimalPlaces(unitPrice, "unitPrice");
    }

    public static void ValidateTaxRatePercent(decimal taxRatePercent)
    {
        if (taxRatePercent < 0m || taxRatePercent > MaxTaxRatePercent)
        {
            throw new EstimateValidationException(
                "taxRatePercent",
                $"Tax rate must be between 0 and {MaxTaxRatePercent} percent.");
        }

        ValidateDecimalPlaces(taxRatePercent, "taxRatePercent");
    }

    private static void ValidateDecimalPlaces(decimal value, string field)
    {
        decimal rounded = Math.Round(value, MaxInputDecimalPlaces);
        if (rounded != value)
        {
            throw new EstimateValidationException(
                field,
                $"'{field}' must have at most {MaxInputDecimalPlaces} decimal places.");
        }
    }
}
