using System;
using System.Globalization;
using System.Text;
using System.Windows.Data;

namespace SystemeCaisse.UI.Converters
{
    /// <summary>
    /// Formats phone numbers with spaces between digit pairs for readability.
    /// Example: "0612345678" → "06 12 34 56 78"
    /// </summary>
    public class PhoneSpacingConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string phone || string.IsNullOrWhiteSpace(phone))
                return value ?? string.Empty;

            // Strip existing spaces/dashes to normalize
            var digits = new StringBuilder();
            foreach (char c in phone)
            {
                if (char.IsDigit(c) || c == '+')
                    digits.Append(c);
            }

            string clean = digits.ToString();
            if (clean.Length < 4) return clean;

            // Group digits in pairs, starting from the left
            var result = new StringBuilder();
            for (int i = 0; i < clean.Length; i++)
            {
                if (i > 0 && i % 2 == 0)
                    result.Append(' ');
                result.Append(clean[i]);
            }

            return result.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Strip spaces to get raw phone number back
            if (value is string formatted)
                return formatted.Replace(" ", "");
            return value ?? string.Empty;
        }
    }
}
