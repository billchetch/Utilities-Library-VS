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
        [AttributeUsage(AttributeTargets.Property)]
        public class PropertyAttribute : Attribute
        {
            public const int NONE = 0;
            public const int EVENT = 1;
            public const int SERIALIZABLE = 2;
            public const int DATA = 4;
            public const int HIDDEN = 8;
            public const int NON_HIDDEN = EVENT | SERIALIZABLE | DATA;

            private int _attributes = NONE;

            public bool IsEvent => HasAttribute(EVENT);
            public bool IsSerializable => HasAttribute(SERIALIZABLE);

            public bool IsData => HasAttribute(DATA);

            public bool IsHidden => HasAttribute(HIDDEN);


            Object _defaultValue;
            public Object DefaultValue => _defaultValue;

            bool _hasDefaultValue = false;
            public bool HasDefaultValue => _hasDefaultValue;

            public PropertyAttribute(int attributtes)
            {
                _attributes = attributtes;
            }

            public PropertyAttribute(int attributtes, Object defaultValue)
            {
                _attributes = attributtes;
                _defaultValue = defaultValue;
                _hasDefaultValue = true;
            }

            public bool HasAttribute(int att)
            {
                return (_attributes & att) > 0;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public DateTime LastModified { get; set; }

        private bool _raiseOnlyIfNotEqual = true;

        private Dictionary<String, Object> _values = new Dictionary<String, Object>();

        public List<String> ChangedProperties { get; internal set; } = new List<String>();

        public bool HasChanged => ChangedProperties.Count > 0;

        public DataSourceObject(bool raiseIfNotEqual)
        {
            _raiseOnlyIfNotEqual = raiseIfNotEqual;
            initialise();
        }

        public DataSourceObject()
        {
            initialise();
        }

        private void initialise()
        {
            Type type = GetType();
            var properties = type.GetProperties();
            foreach (var prop in properties)
            {
                var atts = prop.GetCustomAttributes(typeof(PropertyAttribute), true);
                if (atts.Length == 0) continue;
                PropertyAttribute pa = (PropertyAttribute)atts[0];
                if (pa.HasDefaultValue)
                {
                    Set(pa.DefaultValue, prop.Name, false);
                }
            }
        }
        
        protected void NotifyPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] String propertyName = "", Object newValue = null, Object oldValue = null)
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

        public void Set(Object value, [System.Runtime.CompilerServices.CallerMemberName] String propertyName = "value", bool notify = true)
        {
            Object oldValue = _values.ContainsKey(propertyName) ? _values[propertyName] : null;
            _values[propertyName] = value;

            bool equal = EqualValues(oldValue, value);
            if (notify)
            {
                if (!_raiseOnlyIfNotEqual)
                {
                    NotifyPropertyChanged(propertyName, value, oldValue);
                }
                else if (!equal)
                {
                    NotifyPropertyChanged(propertyName, value, oldValue);
                }
            }
            if (!equal)
            {
                if (!ChangedProperties.Contains(propertyName)) ChangedProperties.Add(propertyName);
            }

            LastModified = DateTime.Now;
        }

        public void Set(Object value, bool notify, [System.Runtime.CompilerServices.CallerMemberName] String propertyName = "value")
        {
            Set(value, propertyName, notify);
        }

        public T Get<T>([System.Runtime.CompilerServices.CallerMemberName] String propertyName = "value")
        {
            return _values.ContainsKey(propertyName) ? (T)_values[propertyName] : default(T);
        }

        public bool HasValue(String propertyName)
        {
            return _values.ContainsKey(propertyName);
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
            Type type = GetType();
            var properties = type.GetProperties();
            foreach (var prop in properties)
            {
                var atts = prop.GetCustomAttributes(typeof(PropertyAttribute), true);
                if (atts.Length == 0) continue;

                PropertyAttribute pa = (PropertyAttribute)atts[0];
                if (pa.IsSerializable)
                {
                    destination[prop.Name] = _values[prop.Name];
                }
            }
        }

        virtual public void Deserialize(Dictionary<String, Object> source, bool notify = false)
        {
            foreach (var kvp in source)
            {
                if (PropertyIsSerializable(kvp.Key))
                {
                    Set(kvp.Value, kvp.Key, notify);
                }
            }
        }

        public void ClearChanged()
        {
            ChangedProperties.Clear();
        }

        public List<Object> GetValues()
        {
            return _values.Values.ToList();
        }

        public List<String> GetPropertyNames(int withAttributes = PropertyAttribute.NON_HIDDEN)
        {
            List<String> propertyNames = new List<String>();

            var properties = GetType().GetProperties();
            foreach (var prop in properties)
            {
                if (withAttributes == -1)
                {
                    propertyNames.Add(prop.Name);
                }
                else
                {
                    var atts = prop.GetCustomAttributes(typeof(PropertyAttribute), true);
                    if (atts.Length == 0) continue;
                    var pa = (PropertyAttribute)atts[0];
                    if (pa.HasAttribute(withAttributes))
                    {
                        propertyNames.Add(prop.Name);
                    }
                }
            }

            return propertyNames;
        }

        public PropertyAttribute GetPropertyAttribute(String propertyName)
        {
            Type type = GetType();
            var properties = type.GetProperties();
            foreach (var prop in properties)
            {
                if (prop.Name == propertyName)
                {
                    var atts = prop.GetCustomAttributes(typeof(PropertyAttribute), true);
                    return atts.Length > 0 ? (PropertyAttribute)atts[0] : null;
                }
            }
            return null;
        }

        public bool PropertyHasAttribute(String propertyName, int attributes)
        {
            var pa = GetPropertyAttribute(propertyName);
            return pa == null ? false : pa.HasAttribute(attributes);
        }

        public bool PropertyIsEvent(String propertyName)
        {
            return PropertyHasAttribute(propertyName, PropertyAttribute.EVENT);
        }

        public bool PropertyIsData(String propertyName)
        {
            return PropertyHasAttribute(propertyName, PropertyAttribute.DATA);
        }

        public bool PropertyIsSerializable(String propertyName)
        {
            return PropertyHasAttribute(propertyName, PropertyAttribute.SERIALIZABLE);
        }

    }
}
