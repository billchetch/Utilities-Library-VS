using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Chetch.Utilities
{
    public class DeviceManager
    {
        public class DeviceInfo
        {
            public enum DeviceStatus
            {
                ANY,
                STARTED,
                DISCONNECTED,
                STOPPED,
                DISABLED
            }

            private Dictionary<String, String> _info = new Dictionary<string, string>();

            public void ReadLine(String line)
            {
                String[] ar = line.Split(':');
                if (ar.Length == 2)
                {
                    String key = ar[0].Trim();
                    String value = ar[1].Trim();

                    _info[key] = value;
                }
            }

            public String GetInfo(String key)
            {
                if (_info.ContainsKey(key))
                {
                    return _info[key];
                }
                else
                {
                    return null;
                }
            }

            public String Description
            {
                get
                {
                    return GetInfo("Device Description");
                }
            }

            public String InstanceID
            {
                get
                {
                    return GetInfo("Instance ID");
                }
            }

            public DeviceStatus Status
            {
                get
                {
                    return (DeviceStatus)Enum.Parse(typeof(DeviceStatus), GetInfo("Status"), true);
                }
            }
        }

        public const String PNPUTIL_FILENAME = "pnputil.exe";
        private const String PNPUTIL_COMMAND_ENUM_DEVICES = "enum-devices";
        private const String PNPUTIL_COMMAND_DISABLE_DEVICE = "disable-device";
        private const String PNPUTIL_COMMAND_ENABLE_DEVICE = "enable-device";
        private const String PNPUTIL_COMMAND_RESTART_DEVICE = "restart-device";

        static private DeviceManager _instance = null;

        static public DeviceManager GetInstance()
        {
            if (_instance == null)
            {
                _instance = new DeviceManager();
            }
            _instance.Populate();
            return _instance;
        }

        List<DeviceInfo> _devices = new List<DeviceInfo>();
        private DeviceInfo _lastDeviceInfo = null;


        private Process pnputil(String command, String args = null)
        {
            Win32.DisableWow64FsRedirection();

            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = PNPUTIL_FILENAME;
            startInfo.Arguments = "/" + command + (args != null ? " " + args : "");
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.CreateNoWindow = true;

            Process proc = new Process();
            proc.StartInfo = startInfo;
            proc.Start();
            //proc.WaitForExit();
            return proc;
        }

        public void Populate()
        {
            _devices.Clear();

            var proc = pnputil(PNPUTIL_COMMAND_ENUM_DEVICES);

            int lineCount = 0;
            while (!proc.StandardOutput.EndOfStream)
            {
                String line = proc.StandardOutput.ReadLine();
                if (lineCount++ == 0) continue; //ignore first line

                if (line == null || line == String.Empty)
                {
                    if (_lastDeviceInfo != null)
                    {
                        _devices.Add(_lastDeviceInfo);
                    }
                    _lastDeviceInfo = new DeviceInfo();

                }
                else
                {
                    _lastDeviceInfo.ReadLine(line);
                }
            }
        }

        public void Refresh()
        {
            Populate();
        }

        public List<DeviceInfo> GetDevices(String searchOn = null, DeviceInfo.DeviceStatus status = DeviceInfo.DeviceStatus.ANY)
        {
            if (searchOn != null) searchOn = searchOn.ToLower();

            List<DeviceInfo> devices2return = new List<DeviceInfo>();
            foreach (var devInfo in _devices)
            {
                if (searchOn != null)
                {
                    String desc = devInfo.Description;
                    if (desc == null || desc.ToLower().IndexOf(searchOn) == -1) continue;
                }

                if (status != DeviceInfo.DeviceStatus.ANY)
                {
                    if (devInfo.Status!= status) continue;
                }

                devices2return.Add(devInfo);
            }

            return devices2return;
        }

        public DeviceInfo GetDevice(String instanceID)
        {
            foreach(DeviceInfo devInfo in _devices)
            {
                if (devInfo.InstanceID == instanceID) return devInfo;
            }
            return null;
        }

        public Process EnableDevice(String instanceID)
        {
            return pnputil(PNPUTIL_COMMAND_ENABLE_DEVICE, instanceID);
        }

        public Process DisableDevice(String instanceID)
        {
            return pnputil(PNPUTIL_COMMAND_DISABLE_DEVICE, instanceID);
        }

        public Process ResetDevice(String instanceID, int sleepFor = 500)
        {
            //disable and then enable
            DisableDevice(instanceID);
            System.Threading.Thread.Sleep(sleepFor);
            return EnableDevice(instanceID);
        }

        public Process RestartDevice(String instanceID)
        {
            return pnputil(PNPUTIL_COMMAND_RESTART_DEVICE, instanceID);
        }
    }
}
