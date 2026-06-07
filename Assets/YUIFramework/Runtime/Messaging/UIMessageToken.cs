using System;

namespace YUIFramework
{
    public sealed class UIMessageToken : IDisposable
    {
        private Action _unsubscribe;

        internal UIMessageToken(string messageName, Action unsubscribe)
        {
            MessageName = messageName;
            _unsubscribe = unsubscribe;
        }

        public string MessageName { get; }
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;
            var unsubscribe = _unsubscribe;
            _unsubscribe = null;
            unsubscribe?.Invoke();
        }
    }
}
