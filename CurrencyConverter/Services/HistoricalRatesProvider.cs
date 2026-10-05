using CurrencyConverter.Models;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace CurrencyConverter.Services;

public sealed class HistoricalRatesProvider : IHistoricalRatesProvider
{
    private static readonly ConcurrentDictionary<DateOnly, IReadOnlyDictionary<string, double>> DailyRateCache = new();
    private static readonly SemaphoreSlim DailyRateRequestLimiter = new(6);

    private static readonly IReadOnlyDictionary<string, string> CoinGeckoIds =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["BTC"] = "bitcoin",
            ["ETH"] = "ethereum",
            ["SOL"] = "solana",
            ["XRP"] = "ripple",
            ["ADA"] = "cardano",
            ["DOGE"] = "dogecoin",
            ["LINK"] = "chainlink",
            ["AVAX"] = "avalanche-2",
            ["DOT"] = "polkadot"
        };

    private readonly HttpClient _httpClient;

    public HistoricalRatesProvider(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<IReadOnlyList<RateObservation>> GetHistoryAsync(
        string fromCode,
        bool fromIsCrypto,
        string toCode,
        bool toIsCrypto,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken = default)
    {
        if (start > end)
            throw new ArgumentException("The history start date must be on or before its end date.", nameof(start));

        if (fromIsCrypto == toIsCrypto &&
            string.Equals(fromCode, toCode, StringComparison.OrdinalIgnoreCase))
        {
            var identityRates = new List<RateObservation>();
            for (var date = DateOnly.FromDateTime(start.UtcDateTime);
                 date <= DateOnly.FromDateTime(end.UtcDateTime);
                 date = date.AddDays(1))
            {
                identityRates.Add(new RateObservation(
                    new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)),
                    1));
            }

            return identityRates;
        }

        var codes = new[]
        {
            (Code: fromCode, IsCrypto: fromIsCrypto),
            (Code: toCode, IsCrypto: toIsCrypto)
        };
        var sources = new Dictionary<string, IReadOnlyList<HistoricalCurrencyRate>>(StringComparer.OrdinalIgnoreCase);
        var fiatCodes = codes
            .Where(item => !item.IsCrypto && !string.Equals(item.Code, "USD", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Code.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (fiatCodes.Length > 0)
        {
            Dictionary<string, IReadOnlyList<HistoricalCurrencyRate>> fiatSeries;
            try
            {
                fiatSeries = await GetFiatHistoryAsync(fiatCodes, start, end, cancellationToken);
            }
            catch (HttpRequestException)
            {
                fiatSeries = await GetCurrencyApiHistoryAsync(fiatCodes, start, end, cancellationToken);
            }

            foreach (var (code, points) in fiatSeries)
                sources[code] = points;
        }

        foreach (var item in codes.Where(item => item.IsCrypto).DistinctBy(item => item.Code, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!CoinGeckoIds.TryGetValue(item.Code, out var coinGeckoId))
                    throw new NotSupportedException($"Historical rates are not available for {item.Code}.");

                sources[item.Code] = await GetCryptoHistoryAsync(coinGeckoId, start, end, cancellationToken);
            }
            catch (HttpRequestException) when (CoinGeckoIds.ContainsKey(item.Code))
            {
                var fallbackSeries = await GetCurrencyApiHistoryAsync([item.Code], start, end, cancellationToken);
                sources[item.Code] = fallbackSeries[item.Code];
            }
        }

        var timeline = sources.Values
            .SelectMany(points => points)
            .Select(point => point.Date)
            .Where(date => date >= DateOnly.FromDateTime(start.UtcDateTime) &&
                           date <= DateOnly.FromDateTime(end.UtcDateTime))
            .Distinct()
            .OrderBy(date => date)
            .ToArray();

        var results = new List<RateObservation>(timeline.Length);
        foreach (var date in timeline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fromRate = GetRateOnOrBefore(fromCode, fromIsCrypto, date, sources);
            var toRate = GetRateOnOrBefore(toCode, toIsCrypto, date, sources);
            if (fromRate is not > 0 || toRate is not > 0)
                continue;

            var pairRate = toRate.Value / fromRate.Value;
            if (double.IsFinite(pairRate) && pairRate > 0)
                results.Add(new RateObservation(
                    new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)),
                    pairRate));
        }

        return results;
    }

    private async Task<Dictionary<string, IReadOnlyList<HistoricalCurrencyRate>>> GetCurrencyApiHistoryAsync(
        IReadOnlyCollection<string> codes,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken)
    {
        var firstDate = DateOnly.FromDateTime(start.UtcDateTime);
        var dateCount = DateOnly.FromDateTime(end.UtcDateTime).DayNumber - firstDate.DayNumber + 1;
        var dates = Enumerable.Range(0, dateCount)
            .Select(offset => firstDate.AddDays(offset))
            .ToArray();

        var tasks = dates.Select(async date =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DailyRateCache.TryGetValue(date, out var cachedRates))
                return (Date: date, Rates: cachedRates);

            await DailyRateRequestLimiter.WaitAsync(cancellationToken);
            try
            {
                if (DailyRateCache.TryGetValue(date, out cachedRates))
                    return (Date: date, Rates: cachedRates);

                var url = $"https://cdn.jsdelivr.net/npm/@fawazahmed0/currency-api@{date:yyyy-MM-dd}/v1/currencies/usd.json";
                try
                {
                    using var response = await _httpClient.GetAsync(url, cancellationToken);
                    response.EnsureSuccessStatusCode();
                    await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
                    using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);

                    if (!document.RootElement.TryGetProperty("usd", out var usdRates) ||
                        usdRates.ValueKind != JsonValueKind.Object)
                    {
                        throw new JsonException("The daily currency API response did not contain USD rates.");
                    }

                    var rates = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    foreach (var rateProperty in usdRates.EnumerateObject())
                    {
                        if (rateProperty.Value.TryGetDouble(out var rate) &&
                            double.IsFinite(rate) &&
                            rate > 0)
                        {
                            rates[rateProperty.Name] = rate;
                        }
                    }

                    DailyRateCache[date] = rates;
                    return (Date: date, Rates: (IReadOnlyDictionary<string, double>)rates);
                }
                catch (HttpRequestException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Some dated snapshots are not published; use only dates with actual source data.
                    return (Date: date, Rates: (IReadOnlyDictionary<string, double>)new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase));
                }
            }
            finally
            {
                DailyRateRequestLimiter.Release();
            }
        });

        var dailyRates = await Task.WhenAll(tasks);
        var result = codes.ToDictionary(
            code => code,
            _ => new List<HistoricalCurrencyRate>(),
            StringComparer.OrdinalIgnoreCase);

        foreach (var (date, rates) in dailyRates)
        {
            foreach (var code in codes)
            {
                if (rates.TryGetValue(code, out var rate))
                    result[code].Add(new HistoricalCurrencyRate(date, rate));
            }
        }

        return result.ToDictionary(
            item => item.Key,
            item => (IReadOnlyList<HistoricalCurrencyRate>)item.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    private async Task<Dictionary<string, IReadOnlyList<HistoricalCurrencyRate>>> GetFiatHistoryAsync(
        IReadOnlyCollection<string> codes,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken)
    {
        var startDate = DateOnly.FromDateTime(start.UtcDateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var endDate = DateOnly.FromDateTime(end.UtcDateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var symbols = Uri.EscapeDataString(string.Join(",", codes));
        var url = $"https://api.frankfurter.dev/v1/{startDate}..{endDate}?base=USD&symbols={symbols}";

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);

        var result = codes.ToDictionary(
            code => code,
            _ => (IReadOnlyList<HistoricalCurrencyRate>)new List<HistoricalCurrencyRate>(),
            StringComparer.OrdinalIgnoreCase);

        if (!document.RootElement.TryGetProperty("rates", out var dateRates) ||
            dateRates.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("The fiat history response did not contain dated rates.");
        }

        var collected = codes.ToDictionary(
            code => code,
            _ => new List<HistoricalCurrencyRate>(),
            StringComparer.OrdinalIgnoreCase);

        foreach (var dateEntry in dateRates.EnumerateObject())
        {
            if (!DateOnly.TryParseExact(dateEntry.Name, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                continue;

            foreach (var code in codes)
            {
                if (dateEntry.Value.TryGetProperty(code, out var rateElement) &&
                    rateElement.TryGetDouble(out var rate) &&
                    double.IsFinite(rate) &&
                    rate > 0)
                {
                    collected[code].Add(new HistoricalCurrencyRate(date, rate));
                }
            }
        }

        foreach (var code in codes)
            result[code] = collected[code].OrderBy(point => point.Date).ToArray();
        return result;
    }

    private async Task<IReadOnlyList<HistoricalCurrencyRate>> GetCryptoHistoryAsync(
        string coinGeckoId,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken)
    {
        var url = $"https://api.coingecko.com/api/v3/coins/{Uri.EscapeDataString(coinGeckoId)}/market_chart/range" +
                  $"?vs_currency=usd&from={start.ToUnixTimeSeconds()}&to={end.ToUnixTimeSeconds()}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("prices", out var prices) ||
            prices.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("The crypto history response did not contain price observations.");
        }

        var dailyPrices = new SortedDictionary<DateOnly, (DateTimeOffset Timestamp, double UsdPerToken)>();
        foreach (var price in prices.EnumerateArray())
        {
            if (price.ValueKind != JsonValueKind.Array || price.GetArrayLength() < 2 ||
                !price[0].TryGetDouble(out var timestampMilliseconds) ||
                !price[1].TryGetDouble(out var usdPerToken) ||
                !double.IsFinite(usdPerToken) ||
                usdPerToken <= 0)
            {
                continue;
            }

            var timestamp = DateTimeOffset.FromUnixTimeMilliseconds((long)timestampMilliseconds);
            var date = DateOnly.FromDateTime(timestamp.UtcDateTime);
            if (!dailyPrices.TryGetValue(date, out var existing) || timestamp > existing.Timestamp)
                dailyPrices[date] = (timestamp, usdPerToken);
        }

        // CoinGecko returns USD per token; the converter's shared base is units per USD.
        return dailyPrices
            .Select(item => new HistoricalCurrencyRate(item.Key, 1.0 / item.Value.UsdPerToken))
            .ToArray();
    }

    private static double? GetRateOnOrBefore(
        string code,
        bool isCrypto,
        DateOnly date,
        IReadOnlyDictionary<string, IReadOnlyList<HistoricalCurrencyRate>> sources)
    {
        if (!isCrypto && string.Equals(code, "USD", StringComparison.OrdinalIgnoreCase))
            return 1;

        if (!sources.TryGetValue(code, out var points))
            return null;

        if (isCrypto)
            return points.LastOrDefault(point => point.Date == date)?.RateToUsd;

        // Fiat weekends/holidays use the most recently published business-day rate.
        for (var index = points.Count - 1; index >= 0; index--)
        {
            if (points[index].Date <= date)
                return points[index].RateToUsd;
        }

        return null;
    }
}
