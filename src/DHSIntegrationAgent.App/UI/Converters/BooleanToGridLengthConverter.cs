using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DHSIntegrationAgent.App.UI.Converters
{
    public class BooleanToGridLengthConverter : IValueConverter
    {
    public string TrueValue { get; set; } = "220";
    public string FalseValue { get; set; } = "0";

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            var lengthString = boolValue ? TrueValue : FalseValue;
            
            if (double.TryParse(lengthString, out double length))
            {
                return new GridLength(length);
            }
            
            // If it's "Auto" or "*"
            if (lengthString.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            {
                return GridLength.Auto;
            }
            
            if (lengthString == "*")
            {
                return new GridLength(1, GridUnitType.Star);
            }
        }
        
        return new GridLength(0);
    }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
