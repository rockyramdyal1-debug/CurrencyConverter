using CurrencyConverter.Services;
using CurrencyConverter.ViewModels;

namespace CurrencyConverter;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Register HTTP Client Providers
        builder.Services.AddHttpClient<IFiatRatesProvider, ExchangeRateApiProvider>();
        builder.Services.AddHttpClient<ICryptoRatesProvider, CoinGeckoProvider>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        builder.Services.AddHttpClient<IHistoricalRatesProvider, HistoricalRatesProvider>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // Register Main Unified Service
        builder.Services.AddSingleton<ICurrencyService, CurrencyService>();

        // Register UI and ViewModel
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainPage>();

        return builder.Build();
    }
}