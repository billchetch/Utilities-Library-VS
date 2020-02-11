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

        public static Dictionary<String, Object> ToNameValuePairs(String s, char delimiter, Boolean urldecode)
        {
            var nvp = new Dictionary<String, Object>();
            if(s != null)
            {
                var nvps = s.Split(delimiter);
                foreach(var nv in nvps)
                {
                    var anv = nv.Split('=');
                    if(anv.Length > 0)
                    {
                        String key = urldecode ? HttpUtility.UrlDecode(anv[0]) : anv[0];
                        nvp.Add(key, anv.Length > 1 ? (urldecode ? HttpUtility.UrlDecode(anv[1]) : anv[1]) : null);
                    }
                }
            }

            return nvp;
        }

        public static Dictionary<String, Object> ParseQueryString(String s)
        {
            return ToNameValuePairs(s, '&', true);
        }

        public static void AssignValue<T>(ref T p, String key, Dictionary<String, Object> vals, bool remove)
        {
            if (vals.ContainsKey(key))
            {
                if (p is int)
                {
                    p = (T)(Object)Int32.Parse((String)vals[key]);
                }
                else
                {
                    p = (T)vals[key];
                }

                if (remove)
                {
                    vals.Remove(key);
                }
            }
        }
    }
}
