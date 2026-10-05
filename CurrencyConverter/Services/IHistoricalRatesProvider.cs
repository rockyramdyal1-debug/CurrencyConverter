using CurrencyConverter.Models;

namespace CurrencyConverter.Services;

public interface IHistoricalRatesProvider
{
    Task<IReadOnlyList<RateObservation>> GetHistoryAsync(
        string fromCode,
        bool fromIsCrypto,
        string toCode,
        bool toIsCrypto,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken = default);
}
