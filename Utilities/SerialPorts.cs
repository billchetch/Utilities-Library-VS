using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Management;
using System.IO.Ports;
using System.Diagnostics;
using System.Globalization;

namespace Chetch.Utilities
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
                String[] searchOns = searchOn.Split(',');
                foreach (String p in portList)
                {
                    String[] parts = p.Split(':');
                    if (parts.Length > 1)
                    {
                        foreach (String toFind in searchOns)
                        {
                            if(parts[1].Contains(toFind.Trim()) && !foundPorts.Contains(parts[0])) foundPorts.Add(parts[0]);
                        }
                    }
                }
            }

            return foundPorts;
        }

        static public List<String> ExpandComPortRanges(String portRanges)
        {
            String[] parts = portRanges.Split(',');
            List<String> expandedPorts = new List<String>();
            foreach (String portRange in parts)
            {
                String[] rangeParts = portRange.Split('-');
                if (rangeParts.Length == 2)
                {
                    int start = System.Convert.ToInt16(rangeParts[0].Replace("COM", ""));
                    int end = System.Convert.ToInt16(rangeParts[1].Replace("COM", ""));
                    for (int i = start; i <= end; i++)
                    {
                        expandedPorts.Add("COM" + i);
                    }
                }
                else
                {
                    expandedPorts.Add("COM" + rangeParts[0].Replace("COM", ""));
                }
            }
            return expandedPorts;
        }

        static public bool IsOpen(String port)
        {
            SerialPort sp = new SerialPort(port);
            try
            {
                sp.Open();
                sp.Close();
                return false;
            }
            catch (Exception)
            {
                return true;
            }
        }
    }
}
