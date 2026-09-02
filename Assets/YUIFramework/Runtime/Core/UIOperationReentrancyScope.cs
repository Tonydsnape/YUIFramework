using System;
using System.Collections.Generic;

namespace YUIFramework
{
    internal static class UIOperationReentrancyScope
    {
        internal static readonly object NavigationKey = new object();

        [ThreadStatic]
        private static HashSet<object> _keys;

        public static bool Contains(object key)
        {
            return _keys != null && _keys.Contains(key);
        }

        public static IDisposable Enter(object key, bool includeNavigation)
        {
            _keys ??= new HashSet<object>();
            _keys.Add(key);
            if (includeNavigation)
            {
                _keys.Add(NavigationKey);
            }

            return new Scope(key, includeNavigation);
        }

        private sealed class Scope : IDisposable
        {
            private readonly object _key;
            private readonly bool _includeNavigation;
            private bool _disposed;

            public Scope(object key, bool includeNavigation)
            {
                _key = key;
                _includeNavigation = includeNavigation;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _keys?.Remove(_key);
                if (_includeNavigation)
                {
                    _keys?.Remove(NavigationKey);
                }
            }
        }
    }
}
