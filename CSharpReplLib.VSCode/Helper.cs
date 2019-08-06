using System;
using System.Collections.Generic;
using System.Text;

namespace CSharpReplLib.VSCode
{
    public static class Helper
    {
        internal static IEnumerable<T> Yield<T>(this T obj)
        {
            yield return obj;
        }

        internal static int FindIndex<T>(this T[] array, Func<T, bool> predicate)
        {
            int index = 0;
            for (; index < array.Length && !predicate(array[index]); index++) { }
            return index == array.Length ? -1 : index;
        }

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
