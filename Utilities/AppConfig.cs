using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System.Diagnostics;
using System.Configuration;

namespace Chetch.Utilities.Config
{
    static public class AppConfig
    {
        static private XElement sysDiagnostics;
        static private XElement sources;
        static private XElement sharedListeners;

        static private void ReadConfig()
        {
            string path = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath; //path to app.Config
            XDocument doc = XDocument.Load(path);
            sysDiagnostics = doc.Descendants("system.diagnostics").First<XElement>();
            sources = sysDiagnostics.Descendants("sources").FirstOrDefault<XElement>();
            sharedListeners = sysDiagnostics.Descendants("sharedListeners").FirstOrDefault<XElement>();
        }

        static public List<String> GetSourceListenerAttributes(String listenerType, String attributeKey)
        {
            ReadConfig();

            var listenerAttributes =
                 from e in sources.Descendants("add")
                 where e.Parent.Name.LocalName == "listeners" && e.Attribute("type") != null && e.Attribute("type").Value.IndexOf(listenerType) >= 0
                 select e.Attribute(attributeKey).Value;

            return listenerAttributes.ToList<String>();
        }

        static public List<String> GetSourceListenerAttributes(Type t, String attributeKey)
        {
            return GetSourceListenerAttributes(t.Name, attributeKey);
        }

        static public bool VerifyEventLogSources(String logName, bool createIfRequired = true)
        {
            var sourceNames = GetSourceListenerAttributes(typeof(EventLog), "initializeData");
            bool requiresRestart = false;
            foreach (var source in sourceNames)
            {
                if (!EventLog.SourceExists(source))
                {
                    if (!createIfRequired)
                    {
                        throw new Exception("Source " + source + " does not exist.");
                    }

                    EventLog.CreateEventSource(source, logName);
                    //Console.WriteLine("Creating source " + source + " for log " + logName);
                    EventLog.WriteEntry(source, "Created source");
                    requiresRestart = true;
                }
            }

            return !requiresRestart;
        }
    }
}
