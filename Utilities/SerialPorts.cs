using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Management;
using System.IO.Ports;
using System.Diagnostics;
using System.Globalization;

namespace Utilities
{
    public static class SerialPorts
    {
        public static List<String> Find(String searchOn)
        {
            List<String> foundPorts = new List<String>();

            //loop through available COM ports looking for searchOn
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE Caption like '%(COM%'"))
            {
                var portnames = SerialPort.GetPortNames();
                var ports = searcher.Get().Cast<ManagementBaseObject>().ToList().Select(p => p["Caption"].ToString());
                var portList = portnames.Select(n => n + ": " + ports.FirstOrDefault(s => s.Contains(n))).ToList();
                foreach (String p in portList)
                {
                    String[] parts = p.Split(':');
                    if (parts.Length > 1 && parts[1].Contains(searchOn)) foundPorts.Add(parts[0]);
                }
            }

            return foundPorts;
        }
    }
}
