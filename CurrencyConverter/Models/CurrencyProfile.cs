namespace CurrencyConverter.Models;

public sealed record CurrencyProfile(
    string Code,
    string CurrencyName,
    string CountryName,
    string Description,
    string EconomySummary,
    string HistorySummary,
    string Symbol,
    string CentralBank,
    string Subunit,
    string Denominations,
    string CountriesUsing,
    string PeggedTo)
{
    public string Heading => $"{CurrencyName} Currency";
    public string SourceUrl => $"https://www.oanda.com/currency-converter/en/currencies/majors/{Code.ToLowerInvariant()}/";
    public CurrencyItem CurrencyItem => new(Code, $"{Code} - {CurrencyName}", 0, IsCrypto: false);
    public string Nicknames => Code switch
    {
        "AUD" => "Aussie; buck; dough",
        "GBP" => "Sterling; quid",
        "USD" => "Buck; greenback",
        _ => "None"
    };

    public string CurrenciesPeggedTo => Code switch
    {
        "USD" => "Examples include AED, HKD, SAR, and several Caribbean currencies.",
        "HKD" => "Macanese pataca (MOP).",
        _ => "None listed."
    };
}
