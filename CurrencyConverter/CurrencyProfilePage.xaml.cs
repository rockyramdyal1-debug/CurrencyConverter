using CurrencyConverter.Models;

namespace CurrencyConverter;

public partial class CurrencyProfilePage : ContentPage
{
    public CurrencyProfilePage(CurrencyProfile profile)
    {
        InitializeComponent();
        BindingContext = profile;
    }

    private async void OnCloseClicked(object? sender, EventArgs e) =>
        await Navigation.PopModalAsync();
}
