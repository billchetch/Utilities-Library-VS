using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;

namespace Chetch.Utilities
{
    public class DataSourceObject : INotifyPropertyChanged
    {
        private Dictionary<String, Object> values = new Dictionary<String, Object>();

        public event PropertyChangedEventHandler PropertyChanged;

        public void InvokePropertyChanged(PropertyChangedEventArgs e)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, e);
        }

        public void InvokePropertyChanged(String propertyName)
        {
            InvokePropertyChanged(new PropertyChangedEventArgs(propertyName));
        }

        public void ClearValues()
        {
            values.Clear();
        }

        public void SetValue(String propertyName, Object value, bool notify = true)
        {
            values[propertyName] = value;
            if (notify)
            {
                InvokePropertyChanged(propertyName);
            }
        }

        public int GetIntValue(String propertyName)
        {
            return values.ContainsKey(propertyName) ? (int)values[propertyName] : 0;
        }

        public String GetStringValue(String propertyName)
        {
            return values.ContainsKey(propertyName) ? (String)values[propertyName] : null;
        }

        public Object GetValue(String propertyName)
        {
            return values.ContainsKey(propertyName) ? (String)values[propertyName] : null;
        }

        public void AssignValues(DataSourceObject dso, bool notify = true)
        {
            dso.ClearValues();
            foreach (var kvp in values)
            {
                dso.SetValue(kvp.Key, kvp.Value, notify);
            }
        }

        public void NotifyAll()
        {
            foreach(var k in values.Keys)
            {
                InvokePropertyChanged(k);
            }
        }
    }
}
