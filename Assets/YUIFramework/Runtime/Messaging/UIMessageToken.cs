using System;

namespace YUIFramework
{
    public sealed class UIMessageToken : IDisposable
    {
        private Action _unsubscribe;
        private bool _isDisposing;

        internal UIMessageToken(
            string messageName,
            Type payloadType,
            int priority,
            Action unsubscribe)
        {
            MessageName = messageName;
            PayloadType = payloadType;
            Priority = priority;
            _unsubscribe = unsubscribe;
        }

        public string MessageName { get; }
        public Type PayloadType { get; }
        public int Priority { get; }
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (IsDisposed || _isDisposing)
            {
                return;
            }

            _isDisposing = true;
            try
            {
                _unsubscribe?.Invoke();
                _unsubscribe = null;
                IsDisposed = true;
            }
            finally
            {
                _isDisposing = false;
            }
        }
    }
}
