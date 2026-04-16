using System;
using System.Collections.Generic;

namespace A_Pair.Core.Utilities
{
    public class CircularHistory<T>
    {
        private readonly T[] _buffer;
        private int _index = 0;
        private int _count = 0;

        public CircularHistory(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _buffer = new T[capacity];
        }

        public void Add(T item)
        {
            _buffer[_index] = item;
            _index = (_index + 1) % _buffer.Length;
            if (_count < _buffer.Length) _count++;
        }

        public IEnumerable<T> GetAll()
        {
            for (int i = 0; i < _count; i++)
            {
                int idx = (_index - _count + i) % _buffer.Length;
                if (idx < 0) idx += _buffer.Length;
                yield return _buffer[idx];
            }
        }
    }
}
