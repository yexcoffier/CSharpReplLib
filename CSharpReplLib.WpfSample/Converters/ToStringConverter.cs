using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace CSharpReplLib.WpfSample.Converters
{
	public class ToStringConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is null)
				return null;

			var type = value.GetType();
			if (type == typeof(string))
				return $"\"{value.ToString()}\"";
			if (type.IsValueType)
				return value.ToString();
			return null;
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
	}
}
