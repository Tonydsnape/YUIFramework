using System;
using System.Collections.Generic;
using System.Threading;

namespace YUIFramework
{
    internal static class UIOperationReentrancyScope
    {
        internal static readonly object NavigationKey = new object();

        private static readonly AsyncLocal<HashSet<object>> LocalKeys =
            new AsyncLocal<HashSet<object>>();

        public static bool Contains(object key)
        {
            return LocalKeys.Value != null && LocalKeys.Value.Contains(key);
        }

        public static IDisposable Enter(object key, bool includeNavigation)
        {
            var previous = LocalKeys.Value;
            var keys = previous == null
                ? new HashSet<object>()
                : new HashSet<object>(previous);
            keys.Add(key);
            if (includeNavigation)
            {
                keys.Add(NavigationKey);
            }

            LocalKeys.Value = keys;
            return new Scope(previous);
        }

        private sealed class Scope : IDisposable
        {
            private readonly HashSet<object> _previous;
            private bool _disposed;

            public Scope(HashSet<object> previous)
            {
                _previous = previous;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                LocalKeys.Value = _previous;
            }
        }
    }
}
