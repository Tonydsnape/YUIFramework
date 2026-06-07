using System;

namespace YUIFramework
{
    internal sealed class UIMessageSubscription : IDisposable
    {
        private Action<UIMessageSubscription> _onDisposed;

        public UIMessageSubscription(string messageName, Delegate handler, object owner, Action<UIMessageSubscription> onDisposed)
        {
            MessageName = messageName;
            Handler = handler;
            Owner = owner;
            _onDisposed = onDisposed;
        }

        public string MessageName { get; }
        public Delegate Handler { get; }
        public object Owner { get; }
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;
            var onDisposed = _onDisposed;
            _onDisposed = null;
            onDisposed?.Invoke(this);
        }
    }
}
