using CurrencyConverter.Models;

namespace CurrencyConverter.Services;

public class CurrencyService : ICurrencyService
{
    private static readonly (string Code, string Name)[] SupportedCryptoCurrencies =
    {
        ("BTC", "Bitcoin"),
        ("ETH", "Ethereum"),
        ("SOL", "Solana"),
        ("XRP", "XRP"),
        ("ADA", "Cardano"),
        ("DOGE", "Dogecoin"),
        ("LINK", "Chainlink"),
        ("AVAX", "Avalanche"),
        ("DOT", "Polkadot")
    };

    private readonly IFiatRatesProvider _fiatProvider;
    private readonly ICryptoRatesProvider _cryptoProvider;

    public CurrencyService(IFiatRatesProvider fiatProvider, ICryptoRatesProvider cryptoProvider)
    {
        _fiatProvider = fiatProvider;
        _cryptoProvider = cryptoProvider;
    }

    public async Task<IEnumerable<CurrencyItem>> GetRatesAsync()
    {
        var items = new List<CurrencyItem>();

        var fiatTask = _fiatProvider.GetFiatRatesInUsdAsync();
        var cryptoTask = _cryptoProvider.GetCryptoRatesInUsdAsync();

        await Task.WhenAll(fiatTask, cryptoTask);

        // Map Fiat Currencies
        foreach (var (code, rate) in fiatTask.Result)
        {
            items.Add(new CurrencyItem(code, $"{code} (Fiat)", rate, IsCrypto: false));
        }

        // Always expose known crypto symbols, even when the crypto API is
        // unavailable. Also include every additional symbol returned by the
        // provider so the picker is not limited to this fallback catalog.
        foreach (var (code, name) in SupportedCryptoCurrencies)
        {
            cryptoTask.Result.TryGetValue(code, out var rate);
            items.Add(new CurrencyItem(code, $"{code} - {name} (Crypto)", rate, IsCrypto: true));
        }

        foreach (var (code, rate) in cryptoTask.Result)
        {
            if (string.IsNullOrWhiteSpace(code) ||
                items.Any(item => string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            items.Add(new CurrencyItem(code.ToUpperInvariant(), $"{code.ToUpperInvariant()} (Crypto)", rate, IsCrypto: true));
        }

        return items.OrderBy(i => i.Code);
    }

    public double Convert(double amount, double fromRate, double toRate)
    {
        if (fromRate <= 0) return 0;
        double amountInUsd = amount / fromRate;
        return amountInUsd * toRate;
    }
}