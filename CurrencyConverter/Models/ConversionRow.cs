namespace CurrencyConverter.Models;

public record ConversionRow(
    double Amount,
    string AmountLabel,
    string ResultLabel,
    bool IsReverse = false,
    CurrencyItem? FromCurrency = null,
    CurrencyItem? ToCurrency = null);
