namespace Estimates.Domain;

/// <summary>
/// Base type for expected business-rule failures. The API layer maps each subtype to a
/// typed GraphQL error with a stable <see cref="Code"/>. Unexpected failures must NOT
/// derive from this type so that they are treated as internal errors.
/// </summary>
public abstract class DomainException : Exception
{
    protected DomainException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    protected DomainException(string code, string message, Exception? innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}

/// <summary>Input broke a validation rule, for example a negative quantity.</summary>
public sealed class EstimateValidationException : DomainException
{
    public EstimateValidationException(string field, string message)
        : base(ErrorCodes.ValidationFailed, message)
    {
        Field = field;
    }

    /// <summary>The name of the input field that failed validation, in GraphQL (camelCase) form.</summary>
    public string Field { get; }
}

/// <summary>The operation is not allowed in the estimate's current status.</summary>
public sealed class InvalidEstimateStateException : DomainException
{
    public InvalidEstimateStateException(string code, string message)
        : base(code, message)
    {
    }
}

/// <summary>The line item does not exist on the estimate.</summary>
public sealed class LineItemNotFoundException : DomainException
{
    public LineItemNotFoundException(Guid lineItemId)
        : base(ErrorCodes.LineItemNotFound, $"Line item '{lineItemId}' was not found on this estimate.")
    {
        LineItemId = lineItemId;
    }

    public Guid LineItemId { get; }
}
