using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SafetyVisionMonitor.Converters;

public class TemperatureToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double temperature)
        {
            return temperature switch
            {
                < 50 => new SolidColorBrush(Colors.LightBlue),
                < 65 => new SolidColorBrush(Colors.LightGreen),
                < 75 => new SolidColorBrush(Colors.Orange),
                < 85 => new SolidColorBrush(Colors.OrangeRed),
                _ => new SolidColorBrush(Colors.Red)
            };
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}