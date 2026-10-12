using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using Accounting.Application;
using Accounting.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Accounting.Infrastructure.Providers;

/// <summary>Settings for the mock provider (section "MockAccountingProvider").</summary>
public sealed class MockAccountingProviderOptions
{
    public const string SectionName = "MockAccountingProvider";

    /// <summary>Simulated network delay, so the synchronous flow feels realistic.</summary>
    [Range(0, 10_000)]
    public int SimulatedLatencyMilliseconds { get; set; } = 150;
}

/// <summary>
/// *** MOCK ACCOUNTING PROVIDER - FOR DEVELOPMENT AND DEMONSTRATION ONLY ***
/// It does not talk to any real accounting system. It keeps "invoices" in memory (lost on restart)
/// and returns references such as "MOCK-INV-1A2B3C4D5E6F".
///
/// Behaviour that makes it useful for learning and testing:
/// - Idempotent: the same idempotency key always returns the same reference, like real providers
///   that support idempotency keys (for example Xero's Idempotency-Key header).
/// - Failure simulation: a customer name containing "MOCK-FAIL" makes the provider reject the
///   invoice, so failure handling and retries can be demonstrated without changing configuration.
/// </summary>
public sealed class MockAccountingProvider : IAccountingProvider
{
    public const string FailureTrigger = "MOCK-FAIL";

    private readonly ConcurrentDictionary<string, string> _referencesByIdempotencyKey = new();
    private readonly MockAccountingProviderOptions _options;
    private readonly ILogger<MockAccountingProvider> _logger;

    public MockAccountingProvider(IOptions<MockAccountingProviderOptions> options, ILogger<MockAccountingProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public AccountingProvider Provider => AccountingProvider.Mock;

    public async Task<string> ExportInvoiceAsync(AccountingInvoice invoice, string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        await Task.Delay(_options.SimulatedLatencyMilliseconds, cancellationToken);

        if (invoice.CustomerName.Contains(FailureTrigger, StringComparison.OrdinalIgnoreCase))
        {
            throw new AccountingProviderException(
                ErrorCodes.ProviderRejected,
                $"Mock provider rejected the invoice because the customer name contains '{FailureTrigger}'.");
        }

        string reference = _referencesByIdempotencyKey.GetOrAdd(idempotencyKey, _ => CreateReference());
        _logger.LogInformation(
            "MOCK provider stored invoice {ExternalReference} for estimate {EstimateId} ({Total} {CurrencyCode})",
            reference,
            invoice.EstimateId,
            invoice.Total,
            invoice.CurrencyCode);
        return reference;
    }

    private static string CreateReference()
    {
        return "MOCK-INV-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
    }
}
