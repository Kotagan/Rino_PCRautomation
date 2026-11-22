using System.Globalization;
using System.Windows.Data;

namespace MFAWPF.Helper.Converters;

public class CustomIsEnabledConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        // 始终允许修改/取消选项（不再因运行中锁定）
        return true;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        return [];
    }
}
