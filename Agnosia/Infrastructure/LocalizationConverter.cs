using System.Globalization;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace Agnosia.Infrastructure;

public class LocalizationConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || string.IsNullOrEmpty(key)) return value;

        var prefix = parameter as string ?? "String.Dashboard.Status.";
        var parts = key.Split('|');
        var resourceKey = prefix + parts[0];

        if (Application.Current?.Resources.TryGetResource(resourceKey, null, out var resourceValue) != true || resourceValue is not string localizedFormat) 
            return key;
        return parts.Length > 1 ? string.Format(localizedFormat, parts.Skip(1).Cast<object>().ToArray()) : localizedFormat;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return BindingOperations.DoNothing;
    }
}