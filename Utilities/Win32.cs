using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace Chetch.Utilities
{
    public static class Win32
    {
        public const int ERROR_ATTACHED_DEVICE_NOT_FUNCTIONING = -2147024865;

        [DllImport("Kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool IsWow64Process(IntPtr hProcess, out bool Wow64Process);

        [DllImport("Kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool Wow64DisableWow64FsRedirection(out IntPtr OldValue);

        public static bool DisableWow64FsRedirection()
        {
            bool bWow64 = false;
            IsWow64Process(System.Diagnostics.Process.GetCurrentProcess().Handle, out bWow64);
            if (bWow64)
            {
                IntPtr OldValue = IntPtr.Zero;
                bool bRet = Wow64DisableWow64FsRedirection(out OldValue);
            }
            return bWow64;
        }
    }
}
