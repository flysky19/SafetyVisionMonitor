using System.Globalization;
using System.Windows.Data;

namespace SafetyVisionMonitor.Converters;

public class EditModeToTitleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isEditMode)
        {
            return isEditMode ? "카메라 수정" : "카메라 추가";
        }
        return "카메라 설정";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}