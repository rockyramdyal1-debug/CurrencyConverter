using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CurrencyConverter.Services;

public class CoinGeckoProvider : ICryptoRatesProvider
{
    private static readonly (string CoinGeckoId, string CoinPaprikaId, string Symbol)[] Coins =
    {
        ("bitcoin", "btc-bitcoin", "BTC"),
        ("ethereum", "eth-ethereum", "ETH"),
        ("solana", "sol-solana", "SOL"),
        ("ripple", "xrp-xrp", "XRP"),
        ("cardano", "ada-cardano", "ADA"),
        ("dogecoin", "doge-dogecoin", "DOGE"),
        ("chainlink", "link-chainlink", "LINK"),
        ("avalanche-2", "avax-avalanche", "AVAX"),
        ("polkadot", "dot-polkadot", "DOT")
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<CoinGeckoProvider> _logger;

    public CoinGeckoProvider(HttpClient httpClient, ILogger<CoinGeckoProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CurrencyConverter", "1.0"));
    }

    public async Task<IDictionary<string, double>> GetCryptoRatesInUsdAsync()
    {
        var result = new Dictionary<string, double>();
        try
        {
            string url = "https://api.coingecko.com/api/v3/simple/price?" +
                               "ids=bitcoin,ethereum,solana,ripple,cardano,dogecoin,chainlink,avalanche-2,polkadot" +
                               "&vs_currencies=usd";

            var response = await _httpClient.GetFromJsonAsync<Dictionary<string, Dictionary<string, double>>>(url);

            if (response is not null)
            {
                foreach (var (coinGeckoId, _, symbol) in Coins)
                    AddCrypto(result, response, coinGeckoId, symbol);
            }
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "CoinGecko crypto rates could not be loaded; trying CoinPaprika.");
        }

        var fallbackCoins = Coins.Where(coin => !result.ContainsKey(coin.Symbol)).ToArray();
        var fallbackResults = await Task.WhenAll(fallbackCoins.Select(GetCoinPaprikaRateAsync));
        foreach (var (symbol, rate) in fallbackResults)
        {
            if (rate > 0)
                result[symbol] = rate;
        }

        if (result.Count == 0)
            _logger.LogError("Neither CoinGecko nor CoinPaprika returned usable crypto rates.");

        return result;
    }

    private async Task<(string Symbol, double Rate)> GetCoinPaprikaRateAsync(
        (string CoinGeckoId, string CoinPaprikaId, string Symbol) coin)
    {
        try
        {
            var ticker = await _httpClient.GetFromJsonAsync<CoinPaprikaTicker>(
                $"https://api.coinpaprika.com/v1/tickers/{coin.CoinPaprikaId}");

            if (ticker?.Quotes.TryGetValue("USD", out var quote) == true && quote.Price > 0)
                return (coin.Symbol, 1.0 / quote.Price);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Could not load the {Symbol} fallback rate from CoinPaprika.", coin.Symbol);
        }

        return (coin.Symbol, 0);
    }

    private static void AddCrypto(
        IDictionary<string, double> rates,
        IReadOnlyDictionary<string, Dictionary<string, double>> data,
        string coinId,
        string symbol)
    {
        if (data.TryGetValue(coinId, out var values) &&
            values.TryGetValue("usd", out var usdPrice) &&
            double.IsFinite(usdPrice) &&
            usdPrice > 0)
        {
            // Providers return USD per token; conversion uses tokens per USD.
            rates[symbol] = 1.0 / usdPrice;
        }
    }

    private sealed class CoinPaprikaTicker
    {
        [JsonPropertyName("quotes")]
        public Dictionary<string, CoinPaprikaQuote> Quotes { get; set; } = new();
    }

    private sealed class CoinPaprikaQuote
    {
        [JsonPropertyName("price")]
        public double Price { get; set; }
    }
}