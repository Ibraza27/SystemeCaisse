using System;
using System.Globalization;
using System.Windows.Data;

namespace SystemeCaisse.UI.Converters
{
    public class BoolToUnitConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isPourcentage)
            {
                return isPourcentage ? "%" : "€";
            }
            return "%";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
