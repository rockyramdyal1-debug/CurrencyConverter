namespace CurrencyConverter.Models;

public record LiveRateRow(string Pair, string Rate, CurrencyItem? FromCurrency = null, CurrencyItem? ToCurrency = null);
