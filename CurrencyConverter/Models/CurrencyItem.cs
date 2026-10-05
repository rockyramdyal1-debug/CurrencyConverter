using System.Globalization;

namespace CurrencyConverter.Models;

public record CurrencyItem(
    string Code,      // e.g. USD, BTC, SOL
    string DisplayName, // e.g. "USD - US Dollar" or "BTC - Bitcoin"
    double RateToUsd,  // Conversion factor relative to 1 USD
    bool IsCrypto     // Flag to differentiate asset type
)
{
    public bool IsAvailable => RateToUsd > 0;
    public bool IsFiat => !IsCrypto;

    public string CountryFlag => IsCrypto ? string.Empty : CurrencyDisplayMetadata.GetCountryFlag(Code);
    public string? CountryFlagImageUrl => IsCrypto ? null : CurrencyDisplayMetadata.GetCountryFlagImageUrl(Code);
    public bool HasNoCountryFlagImage => CountryFlagImageUrl is null;
    public bool HasCountryFlagImage => CountryFlagImageUrl is not null;

    public string CurrencyLabel => IsCrypto
        ? DisplayName.Split(" - ", 2, StringSplitOptions.None).Last()
            .Replace(" (Crypto)", string.Empty, StringComparison.Ordinal)
        : CurrencyDisplayMetadata.GetCurrencyName(Code);
    public string FindOtherCurrencyLabel =>
        CurrencyDisplayMetadata.GetFindOtherCurrencyLabel(Code, CurrencyLabel);

    public string CryptoSymbol => IsCrypto
        ? CurrencyDisplayMetadata.GetCryptoSymbol(Code)
        : string.Empty;

    public string CryptoBadgeColor => CurrencyDisplayMetadata.GetCryptoColor(Code);
}

internal static class CurrencyDisplayMetadata
{
    private sealed record CryptoStyle(string Symbol, string Color);

    private static readonly IReadOnlyDictionary<string, CryptoStyle> CryptoStyles =
        new Dictionary<string, CryptoStyle>(StringComparer.OrdinalIgnoreCase)
        {
            ["BTC"] = new("₿", "#F7931A"),
            ["ETH"] = new("◆", "#627EEA"),
            ["SOL"] = new("≋", "#6A39D9"),
            ["XRP"] = new("X", "#23292F"),
            ["ADA"] = new("A", "#0033AD"),
            ["DOGE"] = new("Ð", "#C2A633"),
            ["LINK"] = new("⬡", "#2A5ADA"),
            ["AVAX"] = new("▲", "#E84142"),
            ["DOT"] = new("●", "#E6007A")
        };

    private static readonly IReadOnlyDictionary<string, string> CurrencyRegionOverrides =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = "US",
            ["EUR"] = "EU",
            ["GBP"] = "GB",
            ["JPY"] = "JP",
            ["CNY"] = "CN",
            ["INR"] = "IN",
            ["CAD"] = "CA",
            ["AUD"] = "AU",
            ["NZD"] = "NZ",
            ["CHF"] = "CH",
            ["HKD"] = "HK",
            ["SGD"] = "SG",
            ["KRW"] = "KR",
            ["MXN"] = "MX",
            ["BRL"] = "BR",
            ["ZAR"] = "ZA",
            ["RUB"] = "RU",
            ["TRY"] = "TR",
            ["CLF"] = "CL",
            ["CNH"] = "CN",
            ["FOK"] = "FO",
            ["GGP"] = "GG",
            ["HRK"] = "HR",
            ["IMP"] = "IM",
            ["JEP"] = "JE",
            ["KID"] = "KI",
            ["LSL"] = "LS",
            ["SLL"] = "SL",
            ["TVD"] = "TV",
            ["XCG"] = "CW",
            ["XDR"] = "🌍",
            ["ZWG"] = "ZW",
            ["ZWL"] = "ZW",
            ["XAF"] = "🌍",
            ["XOF"] = "🌍",
            ["XCD"] = "🌍",
            ["XPF"] = "🌍"
        };

    private static readonly IReadOnlyDictionary<string, string> CurrencyFlagsByCode =
        BuildCurrencyFlagsByCode();
    private static readonly IReadOnlyDictionary<string, string> CurrencyNamesByCode =
        BuildCurrencyNamesByCode();
    private static readonly IReadOnlyDictionary<string, string> CurrencyNameOverrides =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CLF"] = "Unidad de Fomento",
            ["CNH"] = "Chinese Yuan (Offshore)",
            ["FOK"] = "Faroese Króna",
            ["GGP"] = "Guernsey Pound",
            ["HRK"] = "Croatian Kuna",
            ["IMP"] = "Manx Pound",
            ["JEP"] = "Jersey Pound",
            ["KID"] = "Kiribati Dollar",
            ["LSL"] = "Lesotho Loti",
            ["SLL"] = "Sierra Leonean Leone",
            ["TVD"] = "Tuvaluan Dollar",
            ["XCG"] = "Caribbean Guilder",
            ["XDR"] = "Special Drawing Rights",
            ["ZWG"] = "Zimbabwe Gold",
            ["ZWL"] = "Zimbabwean Dollar"
        };
    private static readonly IReadOnlyDictionary<string, string> FindOtherCurrencyNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AED"] = "Utd. Arab Emir. Dirham",
            ["AUD"] = "Australian Dollar",
            ["BRL"] = "Brazilian Real",
            ["CHF"] = "Swiss Franc",
            ["CNY"] = "Chinese Yuan Renminbi",
            ["EUR"] = "Euro",
            ["GBP"] = "British Pound",
            ["HKD"] = "Hong Kong Dollar",
            ["INR"] = "Indian Rupee",
            ["JPY"] = "Japanese Yen",
            ["MXN"] = "Mexican Peso",
            ["MYR"] = "Malaysian Ringgit",
            ["PHP"] = "Philippine Peso",
            ["SAR"] = "Saudi Riyal",
            ["SEK"] = "Swedish Krona",
            ["SGD"] = "Singapore Dollar",
            ["THB"] = "Thai Baht",
            ["USD"] = "US Dollar",
            ["ZAR"] = "South African Rand"
        };

    public static string GetCryptoSymbol(string code) =>
        CryptoStyles.TryGetValue(code, out var style) ? style.Symbol : code[..Math.Min(code.Length, 1)];

    public static string GetCryptoColor(string code) =>
        CryptoStyles.TryGetValue(code, out var style) ? style.Color : "#53627A";

    public static string GetCurrencyName(string code) =>
        CurrencyNameOverrides.TryGetValue(code, out var overrideName)
            ? overrideName
            : CurrencyNamesByCode.TryGetValue(code, out var name) ? name : "Fiat currency";

    public static string GetFindOtherCurrencyLabel(string code, string fallback) =>
        FindOtherCurrencyNames.TryGetValue(code, out var name) ? name : fallback;

    public static string GetCountryFlag(string currencyCode)
    {
        if (CurrencyRegionOverrides.TryGetValue(currencyCode, out var region))
            return IsFlagEmoji(region) ? region : ToFlagEmoji(region);

        return CurrencyFlagsByCode.TryGetValue(currencyCode, out var flag) ? flag : "🌐";
    }

    public static string? GetCountryFlagImageUrl(string currencyCode)
    {
        if (currencyCode.Equals("XAF", StringComparison.OrdinalIgnoreCase) ||
            currencyCode.Equals("XCD", StringComparison.OrdinalIgnoreCase) ||
            currencyCode.Equals("XDR", StringComparison.OrdinalIgnoreCase) ||
            currencyCode.Equals("XOF", StringComparison.OrdinalIgnoreCase) ||
            currencyCode.Equals("XPF", StringComparison.OrdinalIgnoreCase))
        {
            return "currency_globe.png";
        }

        var regionCode = CurrencyRegionOverrides.TryGetValue(currencyCode, out var overrideRegion)
            ? overrideRegion
            : CurrencyFlagsByCode.TryGetValue(currencyCode, out var flag)
                ? GetRegionCodeFromFlag(flag)
                : string.Empty;

        if (regionCode.Length != 2 || !regionCode.All(char.IsAsciiLetter))
            return null;

        return $"https://flagcdn.com/w80/{regionCode.ToLowerInvariant()}.png";
    }

    private static IReadOnlyDictionary<string, string> BuildCurrencyFlagsByCode()
    {
        var flags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures)
                     .OrderBy(culture => culture.Name, StringComparer.Ordinal))
        {
            var region = new RegionInfo(culture.Name);
            var currencyCode = region.ISOCurrencySymbol;
            if (!string.IsNullOrWhiteSpace(currencyCode) && !flags.ContainsKey(currencyCode))
                flags.Add(currencyCode, ToFlagEmoji(region.TwoLetterISORegionName));
        }

        return flags;
    }

    private static IReadOnlyDictionary<string, string> BuildCurrencyNamesByCode()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures)
                     .OrderBy(culture => culture.Name, StringComparer.Ordinal))
        {
            var region = new RegionInfo(culture.Name);
            if (!string.IsNullOrWhiteSpace(region.ISOCurrencySymbol) &&
                !names.ContainsKey(region.ISOCurrencySymbol))
            {
                names.Add(region.ISOCurrencySymbol, region.CurrencyEnglishName);
            }
        }

        return names;
    }

    private static string ToFlagEmoji(string regionCode)
    {
        if (regionCode.Length != 2 || !regionCode.All(char.IsAsciiLetter))
            return "🌐";

        return string.Concat(regionCode.ToUpperInvariant().Select(letter =>
            char.ConvertFromUtf32(0x1F1E6 + letter - 'A')));
    }

    private static string GetRegionCodeFromFlag(string flag)
    {
        var runes = flag.EnumerateRunes().ToArray();
        return runes.Length == 2
            ? string.Concat(runes.Select(rune => ((char)(rune.Value - 0x1F1E6 + 'A')).ToString()))
            : string.Empty;
    }

    private static bool IsFlagEmoji(string value) =>
        value.Length > 0 && !value.All(char.IsAsciiLetter);
}