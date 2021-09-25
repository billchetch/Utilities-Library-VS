using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System.Diagnostics;
using System.Configuration;

namespace Chetch.Utilities.Config
{
    //General config stuff goes here

    //This is specific to the app.config file
    static public class AppConfig
    {
        static bool _readConfig = false;
        static private XDocument _configDoc;
        static private XElement _sysDiagnostics;
        static private XElement _sources;
        static private List<String> _sourceNames;
        static private XElement _sharedListeners;

        static public void ReadConfig(bool forceRead = false)
        {
            if (!_readConfig || forceRead)
            {
                string path = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath; //path to app.Config
                _configDoc = XDocument.Load(path);
                _sysDiagnostics = _configDoc.Descendants("system.diagnostics").First<XElement>();
                _sources = _sysDiagnostics.Descendants("sources").FirstOrDefault<XElement>();
                _sharedListeners = _sysDiagnostics.Descendants("sharedListeners").FirstOrDefault<XElement>();

                _sourceNames = new List<String>();
                foreach (var src in _sources.Elements())
                {
                    _sourceNames.Add(src.Attribute("name").Value);
                }
            }
            _readConfig = true;
        }

        static public List<String> SourceNames
        {
            get
            {
                ReadConfig();
                return _sourceNames;
            }

        }

        static public List<String> GetSourceListenerAttributes(String listenerType, String attributeKey)
        {
            ReadConfig();

            var listenerAttributes =
                 from e in _sources.Descendants("add")
                 where e.Parent.Name.LocalName == "listeners" && e.Attribute("type") != null && e.Attribute("type").Value.IndexOf(listenerType) >= 0
                 select e.Attribute(attributeKey).Value;

            return listenerAttributes.ToList<String>();
        }

        static public List<String> GetSourceListenerAttributes(Type t, String attributeKey)
        {
            return GetSourceListenerAttributes(t.Name, attributeKey);
        }

        static public bool VerifyEventLogSources(String logName, bool createIfRequired = true, bool restrictToLocalMachine = true)
        {
            var sourceNames = GetSourceListenerAttributes(typeof(EventLog), "initializeData");
            bool requiresRestart = false;
            foreach (var source in sourceNames)
            {
                bool sourceExists = restrictToLocalMachine ? EventLog.SourceExists(source, ".") : EventLog.SourceExists(source);
                if (sourceExists && restrictToLocalMachine)
                {
                    String sourceLogName = EventLog.LogNameFromSourceName(source, ".");
                    if (String.IsNullOrEmpty(sourceLogName))
                    {
                        EventLog.DeleteEventSource(source);
                        sourceExists = false;
                    } else if(sourceLogName != logName)
                    {
                        throw new Exception(String.Format("Source {0} already belongs to event log {1}", source, sourceLogName));
                    }
                }
                if (!sourceExists)
                {
                    if (!createIfRequired)
                    {
                        throw new Exception("Source " + source + " does not exist.");
                    }

                    EventLog.CreateEventSource(source, logName);
                    //Console.WriteLine("Creating source " + source + " for log " + logName);
                    EventLog.WriteEntry(source, "Created source");
                    requiresRestart = true; //each new source requires application restart so registry can be updated.
                    break;
                }
            }

            return !requiresRestart;
        }
    } //end AppConfig class

    static public class TraceSourceManager
    {
        class TraceSourceData
        {
            public String Name;
            public TraceSource TS;
            public Dictionary<String, TraceListener> Listeners = new Dictionary<String, TraceListener>();
            private Dictionary<String, TraceFilter> _listenerOriginalFilters = new Dictionary<String, TraceFilter>();


            public TraceSourceData(String name)
            {
                TS = new TraceSource(name);

                Name = name;
                for (var i = 0; i < TS.Listeners.Count; i++)
                {
                    var l = TS.Listeners[i];
                    Listeners[l.Name] = l;
                    _listenerOriginalFilters[l.Name] = l.Filter;
                }
            }

            public void SetListenerFilter(String listenerName, TraceFilter filter)
            {
                if (Listeners.ContainsKey(listenerName))
                {
                    Listeners[listenerName].Filter = filter;
                }
            }

            public void SetListenerTraceLevel(String listenerName, SourceLevels level)
            {
                SetListenerFilter(listenerName, new EventTypeFilter(level));
            }

            public void TurnOffListener(String listenerName)
            {
                SetListenerTraceLevel(listenerName, SourceLevels.Off);
            }

            public void RestoreListenerFilter(String listenerName)
            {
                if (Listeners.ContainsKey(listenerName))
                {
                    Listeners[listenerName].Filter = _listenerOriginalFilters[listenerName];
                }
            }
        }

        static private Dictionary<String, TraceSourceData> _traceSources = null;
        
        /// <summary>
        /// This will instantiate trace sources and then trace this to the specified source.  Pass null if you don't want to trace.
        /// Note that trace sources of the same name share the same Listeners, so they are effectively distinguished by name rather than instance
        /// hence keeping them in a dictionary andnot a list
        /// </summary>
        /// <param name="traceTo"></param>
        /// <param name="eventId"></param>
        static public void InitFromAppConfig(String traceTo, int eventId = 0)
        {
            AppConfig.ReadConfig();

            var traceOutput = new List<String>();
            if (_traceSources == null)
            {
                _traceSources = new Dictionary<String, TraceSourceData>();
                foreach (var sn in AppConfig.SourceNames)
                {
                    _traceSources[sn] = new TraceSourceData(sn);
                    traceOutput.Add(String.Format("Creating TraceSource instance for source {0}", sn));
                }
            }

            if (traceTo != null && _traceSources.ContainsKey(traceTo))
            {
                var ts = _traceSources[traceTo].TS;
                foreach (var o in traceOutput)
                {
                    ts.TraceEvent(TraceEventType.Information, eventId, o);
                }
            }
        }

        static public TraceSource GetInstance(String name)
        {
            if (_traceSources == null)
            {
                _traceSources = new Dictionary<String, TraceSourceData>();
            }

            if (!_traceSources.ContainsKey(name))
            {
                _traceSources[name] = new TraceSourceData(name);
            }

            return _traceSources[name].TS;
        }

        
        private static TraceSourceData GetTraceSourceData(String tsName)
        {
            if (!_traceSources.ContainsKey(tsName))
            {
                throw new Exception(String.Format("Cannot find TraceSoure {0}", tsName));
            }
            return _traceSources[tsName];
        }

        /// <summary>
        /// Methods for all trace sources
        /// </summary>
        /// <returns></returns>
        //static public List<String> GetListenerNames(String tsName)
        static public List<String> GetListenerNames(String tsname)
        {
            List<String> listenerNames = new List<String>();
            var tsd = GetTraceSourceData(tsname);
            foreach (var ln in tsd.Listeners.Keys)
            {
                if (!listenerNames.Contains(ln))
                {
                        listenerNames.Add(ln);
                }
            }
            return listenerNames;
        }

        //static public void SetListenersTraceLevel(String tsName, String listenerName, SourceLevels level)
        static public void SetListenersTraceLevel(String tsname, String listenerName, SourceLevels level)
        {
            var tsd = GetTraceSourceData(tsname);
            tsd.SetListenerTraceLevel(listenerName, level);
        }

        //static public void TurnOffListeners(String tsName, String listenerName)
        static public void TurnOffListeners(String tsname, String tsName, String listenerName)
        {
            var tsd = GetTraceSourceData(tsname);
            tsd.TurnOffListener(listenerName);
        }

        //static public void RestoreListeners(String tsName, String listenerName)
        static public void RestoreListeners(String tsname, String listenerName)
        {
            var tsd = GetTraceSourceData(tsname);
            tsd.RestoreListenerFilter(listenerName);
        }

        /// <summary>
        /// Methods for all trace sources
        /// </summary>
        /// <returns></returns>
        static public List<String> GetListenerNames()
        {
            List<String> listenerNames = new List<String>();
            foreach (var tsd in _traceSources.Values)
            {
                foreach(var ln in tsd.Listeners.Keys)
                {
                    if (!listenerNames.Contains(ln))
                    {
                        listenerNames.Add(ln);
                    }
                }
            }
            return listenerNames;
        }

        static public void SetListenersTraceLevel(String listenerName, SourceLevels level)
        {
            foreach (var tsd in _traceSources.Values)
            {
                tsd.SetListenerTraceLevel(listenerName, level);
            }
        }

        static public void TurnOffListeners(String listenerName)
        {
            foreach (var tsd in _traceSources.Values)
            {
                tsd.TurnOffListener(listenerName);
            }
        }

        static public void RestoreListeners(String listenerName)
        {
            foreach (var tsd in _traceSources.Values)
            {
                tsd.RestoreListenerFilter(listenerName);
            }
        }
    } //end TraceSourceManager class
}
