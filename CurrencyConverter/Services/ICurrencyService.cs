using CurrencyConverter.Models;

namespace CurrencyConverter.Services;

public interface ICurrencyService
{
    Task<IEnumerable<CurrencyItem>> GetRatesAsync();
    double Convert(double amount, double fromRate, double toRate);
}