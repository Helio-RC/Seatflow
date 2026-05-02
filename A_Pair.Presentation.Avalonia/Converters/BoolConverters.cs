using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace A_Pair.Presentation.Avalonia.Converters;

public static class BoolConverters
{
    public static readonly IValueConverter TrueToVisible = new BoolToDoubleConverter(1 , 0);
    public static readonly IValueConverter FalseToVisible = new BoolToDoubleConverter(0 , 1);
    public static readonly IValueConverter CompactPanelWidth = new BoolToDoubleConverter(80 , double.NaN);

    private class BoolToDoubleConverter (double trueValue , double falseValue) : IValueConverter
    {
        public object? Convert (object? value , Type targetType , object? parameter , CultureInfo culture)
            => value is true ? trueValue : falseValue;

        public object? ConvertBack (object? value , Type targetType , object? parameter , CultureInfo culture)
            => throw new NotSupportedException();
    }
}
