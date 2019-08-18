using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace CSharpReplLib.WpfSample.Converters
{
	public class ToFriendlyNameTypeConverter : IValueConverter
	{
		// https://stackoverflow.com/questions/4185521/c-sharp-get-generic-type-name/26429045
		private static string GetFriendlyName(Type type)
		{
			string friendlyName = type.Name;
			if (type.IsGenericType)
			{
				int iBacktick = friendlyName.IndexOf('`');
				if (iBacktick > 0)
				{
					friendlyName = friendlyName.Remove(iBacktick);
				}
				friendlyName += "<";
				Type[] typeParameters = type.GetGenericArguments();
				for (int i = 0; i < typeParameters.Length; ++i)
				{
					string typeParamName = GetFriendlyName(typeParameters[i]);
					friendlyName += (i == 0 ? typeParamName : "," + typeParamName);
				}
				friendlyName += ">";
			}

			return friendlyName;
		}

		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value == null)
				return string.Empty;

			if (!(value is Type type))
				throw new ArgumentException("Require a value of type Type");

			return GetFriendlyName(type);
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
	}
}
