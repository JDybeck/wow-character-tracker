using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WoWCharacterTracker.Ui;

sealed class BoolToVisibility : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is true) != Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// true <-> false. Keeps a menu's button from reopening the menu its own click just closed.
sealed class Not : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

static class PageMatch
{
    // The value is one of the parameter's names, "a" or "a|b|c" (a tab that covers several pages).
    public static bool Matches(object? value, object? parameter) =>
        value?.ToString() is { } v && (parameter?.ToString() ?? "").Split('|').Contains(v);
}

// Visible when the bound value is one of the ConverterParameter's names, e.g. the page name.
sealed class EqualsToVisibility : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        PageMatch.Matches(value, parameter) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// "active" when the bound value is one of the ConverterParameter's names (for a tab's Tag: the open page).
sealed class ActiveIf : IValueConverter
{
    public object? Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        PageMatch.Matches(value, parameter) ? "active" : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// A number times the ConverterParameter, e.g. half of a panel's height for a MaxHeight.
sealed class Times : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double d && double.TryParse(parameter?.ToString(), CultureInfo.InvariantCulture, out var f) ? d * f : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

sealed class NullToVisibility : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        (value is null or "") == Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
