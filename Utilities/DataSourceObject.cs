using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;

namespace Chetch.Utilities
{
    public class DSOPropertyChangedEventArgs : PropertyChangedEventArgs
    {
        public Object NewValue { get; internal set; }
        public Object OldValue { get; internal set; }

        public DSOPropertyChangedEventArgs(String propertyName, Object newValue, Object oldValue) : base(propertyName)
        {
            NewValue = newValue;
            OldValue = oldValue;
        }
    }

    public class DataSourceObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public DateTime LastModified { get; set; }

        private bool _raiseOnlyIfNotEqual = true;

        private Dictionary<String, Object> _values = new Dictionary<String, Object>();

        public List<String> Serializable { get; internal set; } = new List<String>();


        public List<String> DirtyFields { get; internal set; } = new List<String>();


        public bool IsDirty => DirtyFields.Count > 0;

        public List<String> Properties => _values.Keys.ToList();

        public DataSourceObject(bool raiseIfNotEqual)
        {
            _raiseOnlyIfNotEqual = raiseIfNotEqual;
        }

        public DataSourceObject()
        {
            //empty constructor
        }

        
        private void NotifyPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] String propertyName = "", Object newValue = null, Object oldValue = null)
        {
            PropertyChanged?.Invoke(this, new DSOPropertyChangedEventArgs(propertyName, newValue, oldValue));
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

        public void Set(Object value, [System.Runtime.CompilerServices.CallerMemberName] String propertyName = "value", bool notify = true, bool serializable = true)
        {
            Object oldValue = _values.ContainsKey(propertyName) ? _values[propertyName] : null;
            _values[propertyName] = value;
            
            if (notify)
            {
                bool equal = EqualValues(oldValue, value);
                if (!_raiseOnlyIfNotEqual)
                {
                    NotifyPropertyChanged(propertyName, value, oldValue);
                }
                else if (!equal)
                {
                    NotifyPropertyChanged(propertyName, value, oldValue);
                }
                if (!equal)
                {
                    if(!DirtyFields.Contains(propertyName))DirtyFields.Add(propertyName);
                }
            }

            if(serializable && !Serializable.Contains(propertyName))
            {
                Serializable.Add(propertyName);
            }
            LastModified = DateTime.Now;
        }

        public void Set(Object value, bool notify, bool serializable = true, [System.Runtime.CompilerServices.CallerMemberName] String propertyName = "value")
        {
            Set(value, propertyName, notify, serializable);
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
            foreach (var key in Serializable)
            {
                destination[key] = _values[key];
            }
        }

        virtual public void Deserialize(Dictionary<String, Object> source, bool notify = false)
        {
            foreach (var kvp in source)
            {
                Set(kvp.Value, kvp.Key, notify, true);
            }
        }

        public void Clean()
        {
            DirtyFields.Clear();
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
