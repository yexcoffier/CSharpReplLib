using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CSharpReplLib.WpfSample
{
	public static class Helper
	{
		// https://stackoverflow.com/questions/4185521/c-sharp-get-generic-type-name/26429045
		internal static string GetFriendlyName(this Type type)
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
	}
}
