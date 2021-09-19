﻿using System;
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
            public const int SERIALIZABLE = 1;
            public const int HIDDEN = 2;
            public const int IDENTIFIER = 4;
            public const int DESCRIPTOR = 4;


            private int _attributes = NONE;

            public bool IsSerializable => HasAttribute(SERIALIZABLE);

            
            public bool IsHidden => HasAttribute(HIDDEN);

            public bool IsIdentifier => HasAttribute(IDENTIFIER);

            public bool IsDescriptor => HasAttribute(DESCRIPTOR);


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
            if (_values.ContainsKey(propertyName))
            {
                return (T)_values[propertyName];
            }
            else
            {
                Type type = GetType();
                var properties = type.GetProperties();
                foreach (var prop in properties)
                {
                    if (prop.Name == propertyName) return (T)prop.GetValue(this);
                }

                throw new ArgumentException(String.Format("{0} does not have property {1}", type.ToString(), propertyName));
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
                if (PropertyHasAttribute(kvp.Key, PropertyAttribute.SERIALIZABLE))
                {
                    Set(kvp.Value, kvp.Key, notify);
                }
            }
        }

        public void ClearChanged()
        {
            ChangedProperties.Clear();
        }

        public List<String> GetPropertyNames(int withAttributes)
        {
            List<String> propertyNames = new List<String>();

            var properties = GetType().GetProperties();
            foreach (var prop in properties)
            {
                if (!prop.PropertyType.IsPublic) continue;

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
    }
}
