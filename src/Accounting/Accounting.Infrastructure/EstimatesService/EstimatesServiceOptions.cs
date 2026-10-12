using System.ComponentModel.DataAnnotations;

namespace Accounting.Infrastructure.EstimatesService;

/// <summary>
/// How this service reaches the Estimates service, and the service identity it uses
/// (section "EstimatesService"). ClientSecret must come from a secret store, user secrets or an
/// environment variable (EstimatesService__ClientSecret), never from a committed file.
/// </summary>
public sealed class EstimatesServiceOptions
{
    public const string SectionName = "EstimatesService";

    /// <summary>Base address of the Estimates API, for example "http://localhost:5101".</summary>
    [Required]
    [Url]
    public string BaseAddress { get; set; } = string.Empty;

    /// <summary>The identity provider's OAuth 2.0 token endpoint.</summary>
    [Required]
    [Url]
    public string TokenEndpoint { get; set; } = string.Empty;

    [Required]
    public string ClientId { get; set; } = string.Empty;

    [Required]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Least privilege: the only scope this service identity needs.</summary>
    [Required]
    public string Scope { get; set; } = "estimates:integration.read";

    /// <summary>Timeout for a single HTTP attempt.</summary>
    [Range(1, 60)]
    public int AttemptTimeoutSeconds { get; set; } = 3;

    /// <summary>Timeout for the whole call including retries.</summary>
    [Range(1, 120)]
    public int TotalTimeoutSeconds { get; set; } = 10;

    [Range(0, 5)]
    public int MaxRetryAttempts { get; set; } = 2;
}
