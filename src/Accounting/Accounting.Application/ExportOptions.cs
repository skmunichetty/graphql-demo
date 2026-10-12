using System.ComponentModel.DataAnnotations;

namespace Accounting.Application;

/// <summary>Export behaviour (section "Export").</summary>
public sealed class ExportOptions
{
    public const string SectionName = "Export";

    /// <summary>
    /// Maximum time to wait for the accounting provider. After this the attempt is recorded as
    /// FAILED with ACCOUNTING_PROVIDER_TIMEOUT and can be retried.
    /// </summary>
    [Range(1, 120)]
    public int ProviderTimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// A PENDING export older than this is treated as abandoned (for example the service restarted
    /// mid-call) and may be retried. Must be comfortably longer than the provider timeout.
    /// </summary>
    [Range(10, 3600)]
    public int PendingTimeoutSeconds { get; set; } = 120;
}
