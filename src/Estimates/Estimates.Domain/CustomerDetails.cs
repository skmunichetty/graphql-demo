namespace Estimates.Domain;

/// <summary>
/// Customer details captured on an estimate. This is a snapshot, not a link to a customer
/// record: a customer-management service is out of scope for milestone 1.
/// </summary>
public sealed class CustomerDetails
{
    // Required by EF Core to materialise the owned type.
    private CustomerDetails()
    {
        Name = string.Empty;
    }

    private CustomerDetails(string name, string? email, string? phone)
    {
        Name = name;
        Email = email;
        Phone = phone;
    }

    public string Name { get; private set; }

    public string? Email { get; private set; }

    public string? Phone { get; private set; }

    public static CustomerDetails Create(string? name, string? email, string? phone)
    {
        string validName = EstimateRules.ValidateRequiredText(name, "customerName", EstimateRules.MaxCustomerNameLength);
        string? validEmail = EstimateRules.ValidateOptionalEmail(email, "customerEmail");
        string? validPhone = EstimateRules.ValidateOptionalText(phone, "customerPhone", EstimateRules.MaxCustomerPhoneLength);
        return new CustomerDetails(validName, validEmail, validPhone);
    }
}
