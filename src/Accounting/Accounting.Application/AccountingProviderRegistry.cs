using Accounting.Domain;

namespace Accounting.Application;

/// <summary>
/// Finds the provider implementation for a provider code. Adding Xero later means registering
/// one more <see cref="IAccountingProvider"/>; nothing in the export flow changes (open/closed principle).
/// </summary>
public sealed class AccountingProviderRegistry
{
    private readonly Dictionary<AccountingProvider, IAccountingProvider> _providers;

    public AccountingProviderRegistry(IEnumerable<IAccountingProvider> providers)
    {
        _providers = providers.ToDictionary(provider => provider.Provider);
    }

    public IAccountingProvider Get(AccountingProvider provider)
    {
        if (_providers.TryGetValue(provider, out IAccountingProvider? implementation))
        {
            return implementation;
        }

        throw new UnsupportedProviderException(provider);
    }
}
