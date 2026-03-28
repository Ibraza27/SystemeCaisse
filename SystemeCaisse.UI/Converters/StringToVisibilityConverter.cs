using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SystemeCaisse.UI.Converters
{
    public class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return Visibility.Collapsed;
            
            string stringValue = value?.ToString() ?? string.Empty;
            
            // Handle numeric zero: treat as empty/collapsed
            if (value is decimal d && d == 0) return Visibility.Collapsed;
            if (value is int i && i == 0) return Visibility.Collapsed;
            if (value is double db && db == 0) return Visibility.Collapsed;
            
            // If parameter is provided, check if value is in comma-separated list
            if (parameter is string paramString)
            {
                var allowedValues = paramString.Split(',');
                foreach (var allowed in allowedValues)
                {
                    if (string.Equals(stringValue.Trim(), allowed.Trim(), StringComparison.OrdinalIgnoreCase))
                        return Visibility.Visible;
                }
                return Visibility.Collapsed;
            }

            return string.IsNullOrWhiteSpace(stringValue) || stringValue == "0" ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
