using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace SoulmaskServerManager
{
    [ValueConversion(typeof(int), typeof(bool))]
    public class IntToBoolConverter : MarkupExtension, IValueConverter
    {
        private static IntToBoolConverter _instance;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int i)
                return i != 0;
            if (value is bool b)
                return b;
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? 1 : 0;
            return 0;
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
            => _instance ?? (_instance = new IntToBoolConverter());
    }
}
