using System.Globalization;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace Agnosia.Infrastructure;

public class LocalizationConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || string.IsNullOrEmpty(key))
        {
            return value;
        }

        var prefix = parameter as string ?? "String.Dashboard.Status.";
        
        // Handle composite keys like "GrantedCount|1|5" or "At|12:00:00"
        var parts = key.Split('|');
        var resourceKey = prefix + parts[0];

        if (Application.Current?.Resources.TryGetResource(resourceKey, null, out var resourceValue) == true && resourceValue is string localizedFormat)
        {
            if (parts.Length > 1)
            {
                return string.Format(localizedFormat, parts.Skip(1).Cast<object>().ToArray());
            }
            return localizedFormat;
        }

        return key;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return BindingOperations.DoNothing;
    }
}
