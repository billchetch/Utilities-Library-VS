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
        /// <summary>
        /// Class to provide 'Peek' functionality for reading first byte.
        /// </summary>
        public class SerialPort : System.IO.Ports.SerialPort
        {
            private Object _peekBufferLock = new Object();
            private Queue<int> _peekBuffer = new Queue<int>();

            new public int BytesToRead { get => IsOpen ? _peekBuffer.Count + base.BytesToRead : -1; } //Question: peekbuffer stores -1 which may lead to the wrong value as base.BytesToRead may not contain that

            public SerialPort(String port, int baud) : base(port, baud) { }

            public SerialPort(String port) : base(port) { }

            public int PeekByte()
            {
                lock (_peekBufferLock)
                {
                    int b = base.ReadByte();
                    if (b != -1) _peekBuffer.Enqueue(b);
                    return b;
                }
            }

            new public int ReadByte()
            {
                lock (_peekBufferLock)
                {
                    if (_peekBuffer.Count > 0)
                    {
                        return _peekBuffer.Dequeue();
                    }
                    else
                    {
                        return base.ReadByte();
                    }
                }
            }
        } //end class

        public static List<String> Find(String searchOn)
        {
            List<String> foundPorts = new List<String>();

            //loop through available COM ports looking for searchOn
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE Caption like '%(COM%'"))
            {
                var portnames = SerialPort.GetPortNames();
                var portDescriptions = searcher.Get().Cast<ManagementBaseObject>().ToList().Select(p => p["Caption"].ToString() + " " + p["Manufacturer"]);
                var portDescriptionsList = portnames.Select(n => n + ": " + portDescriptions.FirstOrDefault(s => s.Contains(n))).ToList();
                String[] searchOns = searchOn.Split(',');
                foreach (String p in portDescriptionsList)
                {
                    String[] parts = p.Split(':');
                    if (parts.Length > 1)
                    {
                        foreach (String toFind in searchOns)
                        {
                            String[] toFindParts = toFind.Split('&');
                            int findcount = 0;
                            foreach (String toFindPart in toFindParts)
                            {
                                if (parts[1].Contains(toFindPart.Trim())) findcount++;
                            }
                            if (findcount == toFindParts.Length && !foundPorts.Contains(parts[0]))
                            {
                                foundPorts.Add(parts[0]);
                            }
                        }
                    }
                } //end loooping through possible candidates
            } //end using searcher

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
