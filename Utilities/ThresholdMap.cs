using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chetch.Utilities
{
    public class ThresholdMap<E, V> where E : Enum where V : IComparable
    {
        private Dictionary<E, V> _innerMap = new Dictionary<E, V>();
        public List<E> _sortedKeys = new List<E>();

        public ThresholdMap()
        {
            var vals = Enum.GetValues(typeof(E));

            foreach (var v in vals)
            {
                try
                {
                    _innerMap[(E)v] = (V)v;
                } catch (InvalidCastException)
                {
                    _innerMap[(E)v] = (V)System.Convert.ChangeType(v, typeof(V));
                }
            }
            updateSortedKeys();
        }

        private void updateSortedKeys()
        {
            var l = _innerMap.ToList();
            l.Sort((pair1, pair2) => pair1.Value.CompareTo(pair2.Value));
            _sortedKeys.Clear();
            foreach (var kvp in l)
            {
                _sortedKeys.Add(kvp.Key);
            }
        }

        public V this[E e]
        {
            get { return _innerMap[e]; }
            set
            {
                _innerMap[e] = value;
                updateSortedKeys();
            }
        }

        public E GetValue(V v)
        {
            for (int i = 1; i < _sortedKeys.Count; i++)
            {
                var k = _sortedKeys[i];
                V val = _innerMap[k];
                if (v.CompareTo(val) < 0) return _sortedKeys[i - 1];
            }
            return _sortedKeys[_sortedKeys.Count - 1];
        }
    }
}
