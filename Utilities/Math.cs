using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chetch.Utilities
{
    public static class Math
    {
        static public int GCD(int[] numbers)
        {
            return numbers.Aggregate(GCD);
        }

        static public int GCD(int a, int b)
        {
            a = System.Math.Abs(a);
            b = System.Math.Abs(b);
            return b == 0 ? a : GCD(b, a % b);
        }

        static public int LCM(int[] numbers)
        {
            return numbers.Aggregate(LCM);
        }

        static public int LCM(int a, int b)
        {
            return System.Math.Abs(a * b) / GCD(a, b);
        }
    }
}
