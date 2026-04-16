using System.Collections.Concurrent;
using System.Collections.Generic;

namespace A_Pair.Core.Utilities
{
    public class AttributeBag
    {
        private readonly ConcurrentDictionary<string, object?> _store = new();

        public void Set(string key, object? value) => _store[key] = value;
        public bool TryGet<T>(string key, out T? value)
        {
            if (_store.TryGetValue(key, out var obj) && obj is T t)
            {
                value = t;
                return true;
            }
            value = default;
            return false;
        }

        public IEnumerable<KeyValuePair<string, object?>> GetAll() => _store;
    }
}
