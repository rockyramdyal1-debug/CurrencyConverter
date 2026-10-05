namespace CurrencyConverter.Services;

public interface ICryptoRatesProvider
{
    Task<IDictionary<string, double>> GetCryptoRatesInUsdAsync();
}