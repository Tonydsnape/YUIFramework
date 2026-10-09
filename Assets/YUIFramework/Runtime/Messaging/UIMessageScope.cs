using System;
using System.Collections.Generic;
using System.Threading;

namespace YUIFramework
{
    public sealed class UIMessageScope : IDisposable
    {
        private readonly List<UIMessageToken> _tokens = new List<UIMessageToken>();
        private readonly int _owningThreadId;
        private readonly SynchronizationContext _owningSynchronizationContext;
        private CancellationTokenRegistration _cancellationRegistration;
        private int _disposePosted;

        internal UIMessageScope(
            string name,
            CancellationToken cancellationToken,
            int owningThreadId,
            SynchronizationContext owningSynchronizationContext)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "MessageScope" : name.Trim();
            _owningThreadId = owningThreadId;
            _owningSynchronizationContext = owningSynchronizationContext;
            if (cancellationToken.CanBeCanceled)
            {
                _cancellationRegistration = cancellationToken.Register(
                    state => ((UIMessageScope)state).Dispose(),
                    this);
            }
        }

        public string Name { get; }
        public bool IsDisposed { get; private set; }
        public int SubscriptionCount => IsDisposed ? 0 : _tokens.Count;

        internal void Attach(UIMessageToken token)
        {
            if (token == null)
            {
                return;
            }

            if (IsDisposed)
            {
                token.Dispose();
                throw new ObjectDisposedException(Name);
            }

            _tokens.Add(token);
        }

        public void Dispose()
        {
            if (Environment.CurrentManagedThreadId != _owningThreadId)
            {
                if (_owningSynchronizationContext == null)
                {
                    throw new InvalidOperationException(
                        $"Message scope '{Name}' must be disposed on its owning Unity thread.");
                }

                if (Interlocked.Exchange(ref _disposePosted, 1) == 0)
                {
                    _owningSynchronizationContext.Post(
                        state => ((UIMessageScope)state).DisposeOnOwningThread(),
                        this);
                }

                return;
            }

            DisposeOnOwningThread();
        }

        private void DisposeOnOwningThread()
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;
            _cancellationRegistration.Dispose();
            List<Exception> errors = null;
            for (var index = _tokens.Count - 1; index >= 0; index--)
            {
                try
                {
                    _tokens[index]?.Dispose();
                }
                catch (Exception exception)
                {
                    errors ??= new List<Exception>();
                    errors.Add(exception);
                }
            }

            _tokens.Clear();
            if (errors != null)
            {
                throw new AggregateException(
                    $"Message scope '{Name}' cleanup failed.",
                    errors);
            }
        }
    }
}
