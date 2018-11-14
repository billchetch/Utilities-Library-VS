using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Utilities
{
    public static class Format
    {
        public static String AddSlashes(String s)
        {
            return s.Replace("'", "\\'");
        }

        public static String[] AddSlashes(String[] s)
        {
            if (s == null) return null;
            String[] s2 = new string[s.Length];
            for(int i = 0; i < s.Length; i++)
            {
                s2[i] = AddSlashes(s[i]);
            }

            return s2;
        }
    }
}
