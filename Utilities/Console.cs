using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chetch.Utilities
{
    public static class Console
    {
        static public void PK(String text)
        {
            System.Console.WriteLine(text);
            System.Console.ReadKey(true);
        }
    }
}
