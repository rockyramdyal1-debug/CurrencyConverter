using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace CurrencyConverter.Services;

public class ExchangeRateApiProvider : IFiatRatesProvider
{
    private readonly HttpClient _httpClient;

    public ExchangeRateApiProvider(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<IDictionary<string, double>> GetFiatRatesInUsdAsync()
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<FiatApiResponse>(
                "https://open.er-api.com/v6/latest/USD");
            return response?.Rates ?? new Dictionary<string, double>();
        }
        catch
        {
            return new Dictionary<string, double>();
        }
    }

    public sealed class FiatApiResponse
    {
        public FiatApiResponse()
        {
        }

        [JsonPropertyName("rates")]
        public Dictionary<string, double> Rates { get; set; } = new();
    }
}
