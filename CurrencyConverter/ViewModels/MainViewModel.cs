using CurrencyConverter.Models;
using CurrencyConverter.Services;
using CurrencyConverter.Views;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace CurrencyConverter.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly ICurrencyService _currencyService;
    private readonly IHistoricalRatesProvider _historicalRatesProvider;
    private List<CurrencyItem> _allCurrencies = new();
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private CancellationTokenSource? _historyLoadCancellation;
    private bool _selectingFromCurrency;
    private static readonly string[][] OtherCurrencyCodesByColumn =
    [
        ["AED", "CHF", "GBP", "JPY", "PHP", "SGD", "ZAR"],
        ["AUD", "CNY", "HKD", "MXN", "SAR", "THB"],
        ["BRL", "EUR", "INR", "MYR", "SEK", "USD"]
    ];

    public ObservableCollection<CurrencyItem> AvailableCurrencies { get; } = new();
    public bool HasCurrencies => AvailableCurrencies.Count > 0;
    public bool HasNoCurrencies => !HasCurrencies;
    private bool _isCurrencySelectorOpen;
    public bool IsCurrencySelectorOpen
    {
        get => _isCurrencySelectorOpen;
        private set { _isCurrencySelectorOpen = value; OnPropertyChanged(); }
    }

    private string _currencySelectorTitle = "Choose currency";
    public string CurrencySelectorTitle
    {
        get => _currencySelectorTitle;
        private set { _currencySelectorTitle = value; OnPropertyChanged(); }
    }
    public ObservableCollection<ConversionRow> ForwardConversions { get; } = new();
    public ObservableCollection<ConversionRow> ReverseConversions { get; } = new();
    public ObservableCollection<LiveRateRow> LiveRates { get; } = new();
    public ObservableCollection<PopularConversionRow> PopularConversions { get; } = new();
    public ObservableCollection<CurrencyItem> OtherCurrenciesColumnOne { get; } = new();
    public ObservableCollection<CurrencyItem> OtherCurrenciesColumnTwo { get; } = new();
    public ObservableCollection<CurrencyItem> OtherCurrenciesColumnThree { get; } = new();
    private CurrencyProfile? _selectedOtherCurrencyProfile;
    public CurrencyProfile? SelectedOtherCurrencyProfile
    {
        get => _selectedOtherCurrencyProfile;
        private set
        {
            _selectedOtherCurrencyProfile = value;
            OnPropertyChanged();
        }
    }
    public Func<CurrencyProfile, Task>? CurrencyProfileRequested { get; set; }
    public ObservableCollection<RateObservation> ChartHistory { get; } = new();
    public RateChartDrawable ChartDrawable { get; } = new();

    private int _chartPeriodDays = 30;
    public int ChartPeriodDays
    {
        get => _chartPeriodDays;
        private set
        {
            _chartPeriodDays = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Is30DaySelected));
            OnPropertyChanged(nameof(Is60DaySelected));
            OnPropertyChanged(nameof(Is90DaySelected));
            OnPropertyChanged(nameof(Period30Background));
            OnPropertyChanged(nameof(Period60Background));
            OnPropertyChanged(nameof(Period90Background));
            OnPropertyChanged(nameof(Period30Foreground));
            OnPropertyChanged(nameof(Period60Foreground));
            OnPropertyChanged(nameof(Period90Foreground));
            OnPropertyChanged(nameof(ChartRangeLabel));
            _ = LoadHistoricalRatesAsync();
        }
    }

    public bool Is30DaySelected => ChartPeriodDays == 30;
    public bool Is60DaySelected => ChartPeriodDays == 60;
    public bool Is90DaySelected => ChartPeriodDays == 90;

    private string _historyMessage = "Refresh live rates to start recording this pair's trend.";
    public string HistoryMessage
    {
        get => _historyMessage;
        private set { _historyMessage = value; OnPropertyChanged(); }
    }

    private string _historyMinimum = "—";
    public string HistoryMinimum
    {
        get => _historyMinimum;
        private set { _historyMinimum = value; OnPropertyChanged(); }
    }

    private string _historyAverage = "—";
    public string HistoryAverage
    {
        get => _historyAverage;
        private set { _historyAverage = value; OnPropertyChanged(); }
    }

    private string _historyMaximum = "—";
    public string HistoryMaximum
    {
        get => _historyMaximum;
        private set { _historyMaximum = value; OnPropertyChanged(); }
    }

    private CurrencyItem? _selectedFrom;
    public CurrencyItem? SelectedFrom
    {
        get => _selectedFrom;
        set
        {
            if (ReferenceEquals(_selectedFrom, value)) return;
            _selectedFrom = value;
            OnPropertyChanged();
            OnPairChanged();
            Recalculate();
        }
    }

    private CurrencyItem? _selectedTo;
    public CurrencyItem? SelectedTo
    {
        get => _selectedTo;
        set
        {
            if (ReferenceEquals(_selectedTo, value)) return;
            _selectedTo = value;
            OnPropertyChanged();
            OnPairChanged();
            Recalculate();
        }
    }

    public string PairHeading =>
        SelectedFrom is null || SelectedTo is null
            ? "Convert currencies"
            : $"Convert {SelectedFrom.Code} to {SelectedTo.Code}";

    public string FromCodeHeader => SelectedFrom?.Code ?? "From";
    public string ToCodeHeader => SelectedTo?.Code ?? "To";
    public string ForwardTableHeading => $"Convert {FromCodeHeader} to {ToCodeHeader}";
    public string ReverseTableHeading => $"Convert {ToCodeHeader} to {FromCodeHeader}";
    public string ChartRangeLabel =>
        $"Observed exchange rate for {FromCodeHeader}/{ToCodeHeader} over the last {ChartPeriodDays} days";
    public string ChartStartLabel => ChartDrawable.Observations.Count == 0
        ? $"{ChartPeriodDays} days ago"
        : DateOnly.FromDateTime(ChartDrawable.Observations[0].Timestamp.UtcDateTime).ToString("MMM d");
    public string ChartMinimum => GetChartLabelRate(ChartHistory.MinBy(point => point.Rate));
    public string ChartMiddle => GetChartLabelRate(ChartHistory.Count == 0
        ? null
        : new RateObservation(DateTimeOffset.UtcNow, ChartHistory.Average(point => point.Rate)));
    public string ChartMaximum => GetChartLabelRate(ChartHistory.MaxBy(point => point.Rate));
    public string HistoryFooter => "Historical rates are indicative and may differ from transaction or provider prices.";

    public Color Period30Background => Is30DaySelected ? Color.FromArgb("#E8EDF5") : Colors.Transparent;
    public Color Period60Background => Is60DaySelected ? Color.FromArgb("#E8EDF5") : Colors.Transparent;
    public Color Period90Background => Is90DaySelected ? Color.FromArgb("#E8EDF5") : Colors.Transparent;
    public Color Period30Foreground => Is30DaySelected ? Color.FromArgb("#233A61") : Color.FromArgb("#53627A");
    public Color Period60Foreground => Is60DaySelected ? Color.FromArgb("#233A61") : Color.FromArgb("#53627A");
    public Color Period90Foreground => Is90DaySelected ? Color.FromArgb("#233A61") : Color.FromArgb("#53627A");

    private double _amount = 1.0;
    public double Amount
    {
        get => _amount;
        set { _amount = value; OnPropertyChanged(); Recalculate(); }
    }

    private double _convertedAmount;
    public double ConvertedAmount
    {
        get => _convertedAmount;
        set
        {
            _convertedAmount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ConvertedAmountText));
        }
    }

    public string ConvertedAmountText =>
        SelectedFrom?.IsAvailable == true && SelectedTo?.IsAvailable == true
            ? ConvertedAmount.ToString("N6")
            : "—";

    private string _rateSummary = "Choose a currency pair to view its rate.";
    public string RateSummary
    {
        get => _rateSummary;
        private set { _rateSummary = value; OnPropertyChanged(); }
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set { _errorMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasError)); }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public ICommand RefreshRatesCommand { get; }
    public ICommand SwapCurrenciesCommand { get; }
    public ICommand SetChartPeriodCommand { get; }
    public ICommand SelectPopularConversionCommand { get; }
    public ICommand SelectConversionCommand { get; }
    public ICommand OpenFromCurrencySelectorCommand { get; }
    public ICommand OpenToCurrencySelectorCommand { get; }
    public ICommand CloseCurrencySelectorCommand { get; }
    public ICommand SelectOtherCurrencyCommand { get; }

    public MainViewModel(
        ICurrencyService currencyService,
        IHistoricalRatesProvider historicalRatesProvider)
    {
        _currencyService = currencyService;
        _historicalRatesProvider = historicalRatesProvider;
        RefreshRatesCommand = new Command(async () => await LoadDataAsync());
        SwapCurrenciesCommand = new Command(SwapCurrencies);
        SetChartPeriodCommand = new Command<string>(SetChartPeriod);
        SelectPopularConversionCommand = new Command<string>(SelectPopularConversion);
        SelectConversionCommand = new Command<ConversionRow>(SelectConversion);
        OpenFromCurrencySelectorCommand = new Command(() => OpenCurrencySelector(selectingFrom: true));
        OpenToCurrencySelectorCommand = new Command(() => OpenCurrencySelector(selectingFrom: false));
        CloseCurrencySelectorCommand = new Command(() => IsCurrencySelectorOpen = false);
        SelectOtherCurrencyCommand = new Command<CurrencyItem>(async currency => await SelectOtherCurrencyAsync(currency));
        _ = LoadDataAsync();
    }

    public void SelectCurrency(CurrencyItem currency)
    {
        if (_selectingFromCurrency)
            SelectedFrom = currency;
        else
            SelectedTo = currency;

        IsCurrencySelectorOpen = false;
    }

    private void OpenCurrencySelector(bool selectingFrom)
    {
        _selectingFromCurrency = selectingFrom;
        CurrencySelectorTitle = selectingFrom ? "Choose from currency" : "Choose to currency";
        IsCurrencySelectorOpen = true;
    }

    public async Task LoadDataAsync()
    {
        if (!await _loadLock.WaitAsync(0)) return;

        var selectedFromCode = SelectedFrom?.Code;
        var selectedToCode = SelectedTo?.Code;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            IEnumerable<CurrencyItem> loadedRates = await _currencyService.GetRatesAsync();
            var rates = loadedRates
                .Where(c => !string.IsNullOrWhiteSpace(c.Code))
                .GroupBy<CurrencyItem, string>(c => c.Code, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(c => c.Code)
                .ToList();

            _allCurrencies = rates;
            AvailableCurrencies.Clear();
            foreach (var item in _allCurrencies) AvailableCurrencies.Add(item);
            OnPropertyChanged(nameof(HasCurrencies));
            OnPropertyChanged(nameof(HasNoCurrencies));

            SelectedFrom = FindCurrency(selectedFromCode) ?? _allCurrencies.FirstOrDefault();
            SelectedTo = FindCurrency(selectedToCode) ??
                         _allCurrencies.Skip(1).FirstOrDefault() ??
                         _allCurrencies.FirstOrDefault();

            if (_allCurrencies.Count == 0)
                ErrorMessage = "No currency rates are available. Check your connection and try again.";
            else if (_allCurrencies.All(c => !c.IsAvailable))
                ErrorMessage = "Live rates are unavailable. Currency options are shown, but conversion is disabled until rates load.";
        }
        catch (Exception)
        {
            ErrorMessage = "Rates could not be loaded. Check your connection and try again.";
        }
        finally
        {
            IsLoading = false;
            _loadLock.Release();
            Recalculate();
            _ = LoadHistoricalRatesAsync();
        }
    }

    private void OnPairChanged()
    {
        OnPropertyChanged(nameof(PairHeading));
        OnPropertyChanged(nameof(FromCodeHeader));
        OnPropertyChanged(nameof(ToCodeHeader));
        OnPropertyChanged(nameof(ForwardTableHeading));
        OnPropertyChanged(nameof(ReverseTableHeading));
        OnPropertyChanged(nameof(ChartRangeLabel));
        _ = LoadHistoricalRatesAsync();
    }

    private static string GetChartLabelRate(RateObservation? observation) =>
        observation is null ? "—" : observation.Rate.ToString("N6");

    private void ClearPairDetails()
    {
        ForwardConversions.Clear();
        ReverseConversions.Clear();
        LiveRates.Clear();
        PopularConversions.Clear();
        ChartDrawable.Observations = Array.Empty<RateObservation>();
        OnPropertyChanged(nameof(ChartDrawable));
        OnPropertyChanged(nameof(ChartStartLabel));
        OnPropertyChanged(nameof(ChartMinimum));
        OnPropertyChanged(nameof(ChartMiddle));
        OnPropertyChanged(nameof(ChartMaximum));
        HistoryMinimum = "—";
        HistoryAverage = "—";
        HistoryMaximum = "—";
    }

    private CurrencyItem? FindCurrency(string? code) =>
        code is null ? null : _allCurrencies.FirstOrDefault(c =>
            string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));

    private void Recalculate()
    {
        UpdateOtherCurrencies();

        if (SelectedFrom == null || SelectedTo == null || !double.IsFinite(Amount))
        {
            ConvertedAmount = 0;
            RateSummary = "Choose a currency pair to view its rate.";
            ClearPairDetails();
            return;
        }

        if (!SelectedFrom.IsAvailable || !SelectedTo.IsAvailable)
        {
            ConvertedAmount = 0;
            RateSummary = $"Live rate unavailable for {(!SelectedFrom.IsAvailable ? SelectedFrom.Code : SelectedTo.Code)}.";
            ErrorMessage = $"A live rate is unavailable for {(!SelectedFrom.IsAvailable ? SelectedFrom.Code : SelectedTo.Code)}.";
            ClearPairDetails();
            return;
        }

        ConvertedAmount = _currencyService.Convert(Amount, SelectedFrom.RateToUsd, SelectedTo.RateToUsd);
        var oneUnitRate = _currencyService.Convert(1, SelectedFrom.RateToUsd, SelectedTo.RateToUsd);
        RateSummary = $"1 {SelectedFrom.Code} = {oneUnitRate:N6} {SelectedTo.Code}";
        ErrorMessage = null;
        UpdateConversionTables();
        UpdateLiveAndPopularRates();
    }

    private void UpdateOtherCurrencies()
    {
        var columns = new[]
        {
            OtherCurrenciesColumnOne,
            OtherCurrenciesColumnTwo,
            OtherCurrenciesColumnThree
        };

        for (var columnIndex = 0; columnIndex < columns.Length; columnIndex++)
        {
            var targetColumn = columns[columnIndex];
            targetColumn.Clear();
            foreach (var code in OtherCurrencyCodesByColumn[columnIndex])
            {
                var currency = FindCurrency(code);
                if (currency is not null &&
                    currency.IsAvailable &&
                    !string.Equals(currency.Code, SelectedFrom?.Code, StringComparison.OrdinalIgnoreCase))
                {
                    targetColumn.Add(currency);
                }
            }
        }
    }

    private void SwapCurrencies()
    {
        var from = SelectedFrom;
        SelectedFrom = SelectedTo;
        SelectedTo = from;
    }

    private void SetChartPeriod(string? days)
    {
        if (int.TryParse(days, out var period) && period is 30 or 60 or 90)
            ChartPeriodDays = period;
    }

    private void SelectPopularConversion(string? targetCode)
    {
        if (string.IsNullOrWhiteSpace(targetCode)) return;
        var target = FindCurrency(targetCode);
        if (target is not null) SelectedTo = target;
    }

    private async Task SelectOtherCurrencyAsync(CurrencyItem? currency)
    {
        if (currency is null)
            return;

        SelectedTo = currency;
        SelectedOtherCurrencyProfile = CurrencyProfileCatalog.Get(currency.Code);
        if (SelectedOtherCurrencyProfile is not null && CurrencyProfileRequested is not null)
            await CurrencyProfileRequested(SelectedOtherCurrencyProfile);
    }

    private void SelectConversion(ConversionRow? conversion)
    {
        if (conversion is null) return;
        if (conversion.IsReverse) SwapCurrencies();
        Amount = conversion.Amount;
    }

    private void UpdateConversionTables()
    {
        ForwardConversions.Clear();
        ReverseConversions.Clear();
        if (SelectedFrom is null || SelectedTo is null || !SelectedFrom.IsAvailable || !SelectedTo.IsAvailable)
            return;

        double[] amounts = [1, 5, 10, 25, 50, 100, 500, 1000, 5000, 10000, 50000];
        foreach (var amount in amounts)
        {
            var forward = _currencyService.Convert(amount, SelectedFrom.RateToUsd, SelectedTo.RateToUsd);
            var reverse = _currencyService.Convert(amount, SelectedTo.RateToUsd, SelectedFrom.RateToUsd);
            ForwardConversions.Add(new ConversionRow(
                amount,
                $"{amount:N0} {SelectedFrom.Code}",
                $"{forward:N6} {SelectedTo.Code}",
                FromCurrency: SelectedFrom,
                ToCurrency: SelectedTo));
            ReverseConversions.Add(new ConversionRow(
                amount,
                $"{amount:N0} {SelectedTo.Code}",
                $"{reverse:N6} {SelectedFrom.Code}",
                IsReverse: true,
                FromCurrency: SelectedTo,
                ToCurrency: SelectedFrom));
        }
    }

    private void UpdateLiveAndPopularRates()
    {
        LiveRates.Clear();
        PopularConversions.Clear();
        if (SelectedFrom is null || !SelectedFrom.IsAvailable) return;

        string[] liveCodes = ["EUR", "JPY", "CHF", "GBP", "CAD", "AUD", "BTC", "ETH", "XRP"];
        foreach (var code in liveCodes)
        {
            var target = FindCurrency(code);
            if (target is null || !target.IsAvailable || target.Code == SelectedFrom.Code) continue;
            var rate = _currencyService.Convert(1, SelectedFrom.RateToUsd, target.RateToUsd);
            LiveRates.Add(new LiveRateRow(
                $"{SelectedFrom.Code}/{target.Code}",
                rate.ToString("N6"),
                SelectedFrom,
                target));
        }

        string[] popularCodes = ["USD", "CHF", "JPY", "CNY", "EUR", "HKD", "AUD", "NZD", "CAD", "SEK"];
        var availablePopularConversions = new List<PopularConversion>();
        foreach (var code in popularCodes)
        {
            var target = FindCurrency(code);
            if (target is null || !target.IsAvailable || target.Code == SelectedFrom.Code) continue;
            availablePopularConversions.Add(new PopularConversion(code, $"1 {SelectedFrom.Code} to {code}"));
        }

        for (var index = 0; index < availablePopularConversions.Count; index += 2)
        {
            var left = availablePopularConversions[index];
            var right = index + 1 < availablePopularConversions.Count
                ? availablePopularConversions[index + 1]
                : null;
            PopularConversions.Add(new PopularConversionRow(
                left.TargetCode,
                left.Label,
                right?.TargetCode,
                right?.Label)
            {
                BaseCurrency = SelectedFrom,
                LeftCurrency = FindCurrency(left.TargetCode),
                RightCurrency = right is null ? null : FindCurrency(right.TargetCode)
            });
        }
    }

    private async Task LoadHistoricalRatesAsync()
    {
        _historyLoadCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _historyLoadCancellation = cancellation;
        var cancellationToken = cancellation.Token;

        ChartHistory.Clear();
        ChartDrawable.Observations = Array.Empty<RateObservation>();
        ChartDrawable.EmptyMessage = "Loading historical rates…";
        OnPropertyChanged(nameof(ChartDrawable));
        OnPropertyChanged(nameof(ChartStartLabel));
        OnPropertyChanged(nameof(ChartMinimum));
        OnPropertyChanged(nameof(ChartMiddle));
        OnPropertyChanged(nameof(ChartMaximum));

        if (SelectedFrom is null || SelectedTo is null)
        {
            HistoryMessage = "Choose a currency pair to view historical rates.";
            ChartDrawable.EmptyMessage = "Choose a currency pair to view historical rates.";
            UpdateChartData();
            if (ReferenceEquals(_historyLoadCancellation, cancellation))
                _historyLoadCancellation = null;
            cancellation.Dispose();
            return;
        }

        try
        {
            var end = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
            var start = end.AddDays(-ChartPeriodDays);
            HistoryMessage = $"Loading historical rates from {start:MMM d, yyyy} through {end:MMM d, yyyy}…";
            var observations = await _historicalRatesProvider.GetHistoryAsync(
                SelectedFrom.Code,
                SelectedFrom.IsCrypto,
                SelectedTo.Code,
                SelectedTo.IsCrypto,
                start,
                end,
                cancellationToken);

            if (cancellationToken.IsCancellationRequested ||
                !ReferenceEquals(_historyLoadCancellation, cancellation))
            {
                return;
            }

            var exactStart = DateOnly.FromDateTime(start.UtcDateTime);
            var exactEnd = DateOnly.FromDateTime(end.UtcDateTime);
            var filtered = observations
                .Where(point =>
                {
                    var date = DateOnly.FromDateTime(point.Timestamp.UtcDateTime);
                    return date >= exactStart && date <= exactEnd &&
                           double.IsFinite(point.Rate) && point.Rate > 0;
                })
                .OrderBy(point => point.Timestamp)
                .ToList();

            ChartHistory.Clear();
            foreach (var point in filtered) ChartHistory.Add(point);

            HistoryMessage = filtered.Count == 0
                ? $"No historical rates are available for {SelectedFrom.Code}/{SelectedTo.Code} from {start:MMM d} to {end:MMM d, yyyy}."
                : $"{filtered.Count} daily historical rates · {filtered[0].Timestamp:MMM d, yyyy} to {filtered[^1].Timestamp:MMM d, yyyy}";
            ChartDrawable.EmptyMessage = "No historical rates are available for this pair and period.";
            OnPropertyChanged(nameof(ChartStartLabel));
            OnPropertyChanged(nameof(ChartMinimum));
            OnPropertyChanged(nameof(ChartMiddle));
            OnPropertyChanged(nameof(ChartMaximum));
            UpdateChartData();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested ||
                !ReferenceEquals(_historyLoadCancellation, cancellation))
                return;

            ChartHistory.Clear();
            HistoryMessage = "The historical rate request timed out. Try again later.";
            ChartDrawable.EmptyMessage = "Historical rate request timed out.";
            UpdateChartData();
        }
        catch (NotSupportedException)
        {
            if (cancellationToken.IsCancellationRequested ||
                !ReferenceEquals(_historyLoadCancellation, cancellation))
                return;

            ChartHistory.Clear();
            HistoryMessage = $"Historical rates are not supported for {SelectedFrom.Code} or {SelectedTo.Code}.";
            ChartDrawable.EmptyMessage = "Historical data is not supported for this pair.";
            UpdateChartData();
        }
        catch (HttpRequestException)
        {
            if (cancellationToken.IsCancellationRequested ||
                !ReferenceEquals(_historyLoadCancellation, cancellation))
                return;

            ChartHistory.Clear();
            HistoryMessage = $"Historical rates for {SelectedFrom.Code}/{SelectedTo.Code} could not be loaded. The selected provider may not support this date range or may be temporarily unavailable.";
            ChartDrawable.EmptyMessage = "Historical rates could not be loaded.";
            UpdateChartData();
        }
        catch (System.Text.Json.JsonException)
        {
            if (cancellationToken.IsCancellationRequested ||
                !ReferenceEquals(_historyLoadCancellation, cancellation))
                return;

            ChartHistory.Clear();
            HistoryMessage = "The historical rate provider returned data in an unexpected format.";
            ChartDrawable.EmptyMessage = "Historical rate data was invalid.";
            UpdateChartData();
        }
        finally
        {
            if (ReferenceEquals(_historyLoadCancellation, cancellation))
                _historyLoadCancellation = null;
            cancellation.Dispose();
        }
    }

    private void UpdateChartData()
    {
        var observations = ChartHistory
            .OrderBy(point => point.Timestamp)
            .ToList();
        ChartDrawable.Observations = observations;
        OnPropertyChanged(nameof(ChartDrawable));
        OnPropertyChanged(nameof(ChartStartLabel));

        if (observations.Count == 0)
        {
            HistoryMinimum = "—";
            HistoryAverage = "—";
            HistoryMaximum = "—";
            if (!HistoryMessage.StartsWith("No historical rates", StringComparison.Ordinal) &&
                !HistoryMessage.StartsWith("Historical rates", StringComparison.Ordinal) &&
                !HistoryMessage.StartsWith("The historical rate provider", StringComparison.Ordinal) &&
                !HistoryMessage.StartsWith("The historical rate request", StringComparison.Ordinal) &&
                !HistoryMessage.StartsWith("Choose a currency", StringComparison.Ordinal))
            {
                HistoryMessage = "No dated historical observations are available for this period.";
            }
            return;
        }

        var rates = observations.Select(point => point.Rate).ToList();
        HistoryMinimum = rates.Min().ToString("N6");
        HistoryAverage = rates.Average().ToString("N6");
        HistoryMaximum = rates.Max().ToString("N6");
        HistoryMessage = $"{observations.Count} dated rates in the selected period · {observations[0].Timestamp:MMM d, yyyy} to {observations[^1].Timestamp:MMM d, yyyy}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}