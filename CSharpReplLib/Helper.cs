using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CSharpReplLib
{
    internal static class Helper
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

        internal static T[] ToArrayLocked<T>(this IEnumerable<T> enumerable, object lockEnumerable)
        {
            lock (lockEnumerable)
            {
                return enumerable.ToArray();
            }
        }

        internal static IReadOnlyDictionary<TKey, TValue> ToDictionaryLocked<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, object lockDictionary)
        {
            lock (lockDictionary)
            {
                return dictionary.ToDictionary(kv => kv.Key, kv => kv.Value);
            }
        }
    }
}
