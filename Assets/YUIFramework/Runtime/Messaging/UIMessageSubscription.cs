using System;

namespace YUIFramework
{
    internal abstract class UIMessageSubscription : IDisposable
    {
        private Action<UIMessageSubscription> _onDisposed;

        protected UIMessageSubscription(
            string messageName,
            Type payloadType,
            object owner,
            int priority,
            long sequence,
            Action<UIMessageSubscription> onDisposed)
        {
            MessageName = messageName;
            PayloadType = payloadType;
            Owner = owner;
            Priority = priority;
            Sequence = sequence;
            _onDisposed = onDisposed;
        }

        public string MessageName { get; }
        public Type PayloadType { get; }
        public object Owner { get; }
        public int Priority { get; }
        public long Sequence { get; }
        public bool IsDisposed { get; private set; }
        public abstract Delegate Handler { get; }

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

    internal sealed class UIMessageSubscription<T> : UIMessageSubscription
    {
        private readonly Action<T> _handler;

        public UIMessageSubscription(
            string messageName,
            Action<T> handler,
            object owner,
            int priority,
            long sequence,
            Action<UIMessageSubscription> onDisposed)
            : base(
                messageName,
                typeof(T),
                owner,
                priority,
                sequence,
                onDisposed)
        {
            _handler = handler;
        }

        public override Delegate Handler => _handler;

        public void Invoke(T payload)
        {
            _handler(payload);
        }
    }
}
