using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MFAWPF.Helper.Converters;

/// <summary>
/// Converts a non-null object reference to Visibility.Visible, and null to Visibility.Collapsed (or vice versa).
/// </summary>
public class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isNotNull = value != null;
        if (parameter is string paramStr && paramStr.Equals("Inverse", StringComparison.OrdinalIgnoreCase))
        {
            isNotNull = !isNotNull;
        }

        return isNotNull ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
