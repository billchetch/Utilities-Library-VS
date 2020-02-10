using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace Chetch.Utilities
{
    public static class Convert
    {

        public static String ToNameValuePairsString(Dictionary<String, Object> vals, String delimiter, Boolean urlencode)
        {
            if (vals == null) return null;

            String s = "";
            if (vals.Count > 0)
            {
                int i = 0;
                foreach(var entry in vals)
                {
                    if (entry.Key == null) continue;
                    String p = urlencode ? HttpUtility.UrlEncode(entry.Key) : entry.Key;
                    String v = entry.Value == null ? "" : (urlencode ? HttpUtility.UrlEncode(entry.Value.ToString()) : entry.Value.ToString());
                    s += (i++ > 0 ? delimiter : "") + p + "=" + v;    
                }

            }

            return s;
        }

        public static String ToQueryString(Dictionary<String, Object> vals)
        {
            return ToNameValuePairsString(vals, "&", true);
        }
    }
}
