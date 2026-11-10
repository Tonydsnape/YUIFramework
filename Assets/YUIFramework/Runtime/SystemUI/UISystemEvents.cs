using System;
using System.Collections.Generic;

namespace YUIFramework
{
    internal static class UISystemEvents
    {
        internal static void Publish<T>(Action<T> handlers, T value)
        {
            if (handlers == null) return;
            List<Exception> errors = null;
            foreach (Action<T> handler in handlers.GetInvocationList())
            {
                try { handler(value); }
                catch (Exception exception) { (errors ??= new List<Exception>()).Add(exception); }
            }
            if (errors != null) throw new AggregateException("System UI observer failed.", errors);
        }
    }
}
