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
        public event PropertyChangedEventHandler PropertyChanged;

        public DateTime LastModified { get; set; }

        private bool _raiseOnlyIfNotEqual = true;

        public DataSourceObject(bool raiseIfNotEqual)
        {
            _raiseOnlyIfNotEqual = raiseIfNotEqual;
        }

        public DataSourceObject()
        {
            //empty constructor
        }

        private Dictionary<String, Object> _values = new Dictionary<String, Object>();

        private void NotifyPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] String propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool EqualValues(Object v1, Object v2)
        {
            if(v1 == null && v2 == null)
            {
                return true;
            } else if(v1 == null)
            {
                return v2.Equals(v1);
            } else
            {
                return v1.Equals(v2);
            }
        }

        public void Set(Object value, [System.Runtime.CompilerServices.CallerMemberName] String propertyName = "value", bool notify = true)
        {
            Object oldValue = _values.ContainsKey(propertyName) ? _values[propertyName] : null;
            _values[propertyName] = value;

            if (notify)
            {
                if (!_raiseOnlyIfNotEqual)
                {
                    NotifyPropertyChanged(propertyName);
                }
                else if (!EqualValues(oldValue, value))
                {
                    NotifyPropertyChanged(propertyName);
                }
            }
            LastModified = DateTime.Now;
        }

        public T Get<T>([System.Runtime.CompilerServices.CallerMemberName] String propertyName = "value")
        {
            return _values.ContainsKey(propertyName) ? (T)_values[propertyName] : default(T);
        }

        virtual public void Copy(DataSourceObject dso, bool notify = true)
        {
            foreach (var kvp in _values)
            {
                dso.Set(kvp.Value, kvp.Key, notify);
            }
        }

        virtual public void Serialize(Dictionary<String, Object> destination)
        {
            foreach (var kvp in _values)
            {
                destination[kvp.Key] = kvp.Value;
            }
        }

        public List<Object> GetValues()
        {
            return _values.Values.ToList();
        }

        public List<String> GetPropertyNames()
        {
            return _values.Keys.ToList();
        }

        public bool HasProperty(String propertyName)
        {
            return _values.ContainsKey(propertyName);
        }
    }
}
