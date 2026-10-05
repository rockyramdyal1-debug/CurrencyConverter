using CurrencyConverter.Models;
using CurrencyConverter.ViewModels;

namespace CurrencyConverter
{
    public partial class MainPage : ContentPage
    {
        public MainPage(MainViewModel viewModel)
        {
            InitializeComponent();
            BindingContext = viewModel;
            viewModel.CurrencyProfileRequested = ShowCurrencyProfileAsync;
            RateTrendGraph.Drawable = viewModel.ChartDrawable;
            RateTrendGraph.HeightRequest = 220;
            viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.ChartDrawable))
                    RateTrendGraph.Invalidate();
            };
            RateTrendGraph.SizeChanged += (_, _) => RateTrendGraph.Invalidate();
        }

        private Task ShowCurrencyProfileAsync(CurrencyProfile profile) =>
            Navigation.PushModalAsync(new CurrencyProfilePage(profile));

        private void OnCurrencySelectionChanged(object? sender, SelectionChangedEventArgs args)
        {
            if (args.CurrentSelection.FirstOrDefault() is CurrencyItem currency &&
                BindingContext is MainViewModel viewModel)
            {
                viewModel.SelectCurrency(currency);
            }

            if (sender is CollectionView collectionView)
                collectionView.SelectedItem = null;
        }
    }
}
