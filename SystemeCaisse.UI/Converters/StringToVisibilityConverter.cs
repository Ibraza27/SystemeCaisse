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
            
            string stringValue = value.ToString();
            
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

            return string.IsNullOrWhiteSpace(stringValue) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
