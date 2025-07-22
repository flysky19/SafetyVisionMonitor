using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SafetyVisionMonitor.Converters;

public class UsageToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double usage)
        {
            return usage switch
            {
                < 30 => Colors.Green,
                < 60 => Colors.Orange,
                < 80 => Colors.OrangeRed,
                _ => Colors.Red
            };
        }
        return Colors.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}