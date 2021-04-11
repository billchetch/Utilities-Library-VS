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
                    var anv = nv.Split('='); //assume the equals sign is used to separate key and value
                    if(anv.Length > 0)
                    {
                        if (urldecode)
                        {
                            if (!IsUrlEncoded(anv[0]))
                            {
                                throw new Exception(String.Format("{0} is not URL encoded", anv[0]));
                            }
                            if(anv.Length > 1 && !IsUrlEncoded(anv[1]))
                            {
                                throw new Exception(String.Format("{0} is not URL encoded", anv[1]));
                            }
                        }

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

        public static bool IsUrlEncoded(String s)
        {
            try
            {
                var s1 = s.Replace("%20", "+");
                var d1 = HttpUtility.UrlDecode(s1);
                var e1 = HttpUtility.UrlEncode(d1);
                if (e1.Length != s1.Length)
                {
                    return false;
                }
                else
                {
                    return e1.Equals(s1, StringComparison.OrdinalIgnoreCase);
                }

            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void AssignValue<T>(ref T p, String key, Dictionary<String, Object> vals, bool remove)
        {
            if (vals.ContainsKey(key))
            {
                if (p is int)
                {
                    p = (T)(Object)Int32.Parse(vals[key].ToString());
                }
                else if(p is String)
                {
                    p = (T)(Object)vals[key].ToString();
                } else
                {
                    p = (T)vals[key];
                }

                if (remove)
                {
                    vals.Remove(key);
                }
            }
        }

        public static void AssignValue<T>(ref T p, int position, Object[] vals, bool remove = false)
        {
            if (position < vals.Length)
            {
                if (p is int)
                {
                    p = (T)(Object)Int32.Parse(vals[position].ToString());
                }
                else
                {
                    p = (T)vals[position];
                }

                if (remove)
                {
                    //TODO
                }
            }
        }

        public static String ToString(byte[] bytes)
        {
            char[] chars = new char[bytes.Length];
            for (int i = 0; i < bytes.Length; i++)
            {
                chars[i] = (char)bytes[i];
            }
            return new string(chars);
        }

        public static byte[] ToBytes(String s)
        {
            byte[] bytes = new byte[s.Length];
            for (int i = 0; i < s.Length; i++)
            {
                bytes[i] = (byte)s[i];
            }
            return bytes;
        }

        public static byte[] ToBytes(ValueType n, bool removeZeroBytePadding = true, int padToLength = -1)
        {
            return ToBytes(n, BitConverter.IsLittleEndian, removeZeroBytePadding, padToLength);
        }


        public static byte[] ToBytes(ValueType n, int padToLength)
        {
            return ToBytes(n, BitConverter.IsLittleEndian, true, padToLength);
        }

        public static byte[] ToBytes(ValueType n, bool littleEndian, bool removeZeroBytePadding = true, int padToLength = -1)
        {
            int sz = System.Runtime.InteropServices.Marshal.SizeOf(n);
            if (sz <= sizeof(System.Int64))
            {
                return ToBytes(System.Convert.ToInt64(n), littleEndian, removeZeroBytePadding, padToLength);
            }
            else
            {
                throw new Exception("Size of variable is larger than the system long size of " + sizeof(long));
            }
        }

        public static byte[] ToBytes(Int64 n, bool removeZeroBytePadding = true, int padToLength = -1)
        {
            return ToBytes(n, BitConverter.IsLittleEndian, removeZeroBytePadding, padToLength);
        }

        public static byte[] ToBytes(Int64 n, int padToLength)
        {
            return ToBytes(n, BitConverter.IsLittleEndian, true, padToLength);
        }

        public static byte[] ToBytes(Int64 n, bool littleEndian, bool removeZeroBytePadding = true, int padToLength = -1)
        {
            var bytes = BitConverter.GetBytes(n);

            if ((BitConverter.IsLittleEndian && !littleEndian) || (!BitConverter.IsLittleEndian && littleEndian))
            {
                Array.Reverse(bytes);
            }

            if (removeZeroBytePadding)
            {
                //when n is Zero or when the 'end' byte is non-zero
                if (n == 0) return new byte[] { (byte)0 };
                if (bytes[littleEndian ? bytes.Length - 1 : 0] != 0) return bytes;

                //first significatng byte is not at either end of the array
                int idx = -1;
                for (int i = 0; i < bytes.Length; i++)
                {
                    idx = littleEndian ? bytes.Length - 1 - i : i;
                    if (bytes[idx] != 0)
                    {
                        break;
                    }
                }

                int startIdx = littleEndian ? 0 : idx;
                int endIdx = littleEndian ? idx : bytes.Length - 1;
                var bts = new byte[1 + (endIdx - startIdx)];
                for (int i = startIdx; i <= endIdx; i++)
                {
                    bts[i - startIdx] = bytes[i];
                }
                bytes = bts;
            }

            if(padToLength > bytes.Length)
            {
                byte[] padded = new byte[padToLength];
                Array.Copy(bytes, 0, padded, littleEndian ? 0 : padToLength - bytes.Length, bytes.Length);
                bytes = padded;
            }

            return bytes;
        }

        public static bool ToBoolean(Object obj)
        {
            bool b = false;

            try
            {
                b = System.Convert.ToBoolean(obj);
            }
            catch (System.FormatException)
            {
                int i = System.Convert.ToInt16(obj);
                b = System.Convert.ToBoolean(i);
            }

            return b;
        }

        public static byte ToByte(Object obj)
        {
            return (byte)System.Convert.ToInt16(obj);
        }

        public static long ToLong(byte[] bytes, bool littleEndian = true)
        {
            long n = 0;
            for (int i = 0; i < bytes.Length; i++)
            {
                int idx = littleEndian ? i : bytes.Length - (i + 1);
                long b = (long)bytes[i];
                n += b << 8 * i;
            }
            return n;
        }

        public static int ToInt(byte[] bytes, bool littleEndian = true)
        {
            return (int)ToLong(bytes, littleEndian);
        }

        public static float ToFloat(byte[] bytes, bool littleEndian = true)
        {
            if (BitConverter.IsLittleEndian != littleEndian) Array.Reverse(bytes);
            return BitConverter.ToSingle(bytes, 0);
        }

    } //end of class 
}
