using CurrencyConverter.Models;

namespace CurrencyConverter.Views;

public partial class CurrencyIcon : ContentView
{
    public static readonly BindableProperty CurrencyProperty =
        BindableProperty.Create(
            nameof(Currency),
            typeof(CurrencyItem),
            typeof(CurrencyIcon),
            propertyChanged: OnCurrencyChanged);

    public CurrencyItem? Currency
    {
        get => (CurrencyItem?)GetValue(CurrencyProperty);
        set => SetValue(CurrencyProperty, value);
    }

    public CurrencyIcon()
    {
        InitializeComponent();
    }

    private static void OnCurrencyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is CurrencyIcon icon)
            icon.Content.BindingContext = newValue;
    }
}
