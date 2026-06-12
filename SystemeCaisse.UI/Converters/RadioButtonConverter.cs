using System;
using System.Globalization;
using System.Windows.Data;

namespace SystemeCaisse.UI.Converters
{
    /// <summary>
    /// Two-way converter for binding RadioButtons to a string property.
    /// Convert: returns true if value == parameter.
    /// ConvertBack: returns parameter when IsChecked becomes true.
    /// </summary>
    public class RadioButtonConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null) return false;
            return value.ToString()?.Equals(parameter.ToString(), StringComparison.OrdinalIgnoreCase) ?? false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isChecked && isChecked && parameter != null)
                return parameter.ToString()!;
            return Binding.DoNothing;
        }
    }
}
