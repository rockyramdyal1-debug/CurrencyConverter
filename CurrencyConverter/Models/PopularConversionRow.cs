namespace CurrencyConverter.Models;

public record PopularConversionRow(
    string LeftTargetCode,
    string LeftLabel,
    string? RightTargetCode,
    string? RightLabel)
{
    public bool HasRightConversion => !string.IsNullOrWhiteSpace(RightTargetCode);
    public CurrencyItem? BaseCurrency { get; init; }
    public CurrencyItem? LeftCurrency { get; init; }
    public CurrencyItem? RightCurrency { get; init; }
}
