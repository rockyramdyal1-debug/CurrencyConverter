namespace CurrencyConverter.Services;

public interface IFiatRatesProvider
{
    Task<IDictionary<string, double>> GetFiatRatesInUsdAsync();
}