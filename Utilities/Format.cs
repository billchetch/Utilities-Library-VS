using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Chetch.Utilities
{
    //Formatting class.
    public static class Format
    {
        public static String AddSlashes(String s)
        {
            return s?.Replace("'", "\\'");
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

        public static String RemoveRepeatWhiteSpace(String s)
        {
            if (s == null || s == "") return s;

            String r = s.Trim();
            RegexOptions options = RegexOptions.None;
            Regex regex = new Regex("[ ]{2,}", options);
            r = regex.Replace(r, " ");
            return r;
        }

        public static Dictionary<String, String> ParsCommandLineArguments(String[] args)
        {
            Dictionary<String, String> startArgs = new Dictionary<String, String>();
            String[] optionPrefixes = new string[] { "--", "/" };
            String option = null;
            for (int i = 0; i < args.Length; i++)
            {
                String s = args[i].Trim();
                bool isOption = false;
                foreach (var prefix in optionPrefixes)
                {
                    if (s.IndexOf(prefix) == 0)
                    {
                        option = s.Substring(prefix.Length).ToLower();
                        startArgs[option] = null;
                        isOption = true;
                        break;
                    }
                }
                if (!isOption && i > 0)
                {
                    startArgs[option] = s;
                }
            }
            return startArgs;
        }
    }
}
