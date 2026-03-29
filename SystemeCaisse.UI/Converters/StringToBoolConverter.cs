using System;
using System.Globalization;
using System.Windows.Data;

namespace SystemeCaisse.UI.Converters
{
    /// <summary>
    /// Converts a string to a boolean: returns true if the string is not null/empty/whitespace.
    /// Used in MultiDataTrigger conditions where a Visibility converter cannot be used.
    /// </summary>
    public class StringToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return false;
            string stringValue = value.ToString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(stringValue) && stringValue != "0";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
