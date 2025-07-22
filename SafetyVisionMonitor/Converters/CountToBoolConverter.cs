using System;
using System.Globalization;
using System.Windows.Data;

namespace SafetyVisionMonitor.Converters
{
    public class CountToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count && parameter is string targetCountString)
            {
                if (int.TryParse(targetCountString, out int targetCount))
                {
                    return count == targetCount;
                }
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}