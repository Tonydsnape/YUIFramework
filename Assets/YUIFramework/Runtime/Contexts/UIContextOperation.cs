using System;
using System.Threading;

namespace YUIFramework
{
    internal sealed class UIContextOperation : IDisposable
    {
        private readonly BaseContext _owner;
        private Action _onDisposed;
        private CancellationTokenSource _linkedCancellation;

        public UIContextOperation(
            BaseContext owner,
            UIOperationKind kind,
            CancellationToken externalCancellation,
            CancellationToken lifetimeCancellation,
            CancellationToken serviceCancellation,
            Action onDisposed)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Id = UIOperationId.Next();
            Kind = kind;
            _onDisposed = onDisposed;
            _linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                externalCancellation,
                lifetimeCancellation,
                serviceCancellation);
        }

        public UIOperationId Id { get; }
        public UIOperationKind Kind { get; }
        public CancellationToken Token => _linkedCancellation?.Token ?? CancellationToken.None;

        public void Dispose()
        {
            var cancellation = Interlocked.Exchange(ref _linkedCancellation, null);
            cancellation?.Dispose();
            _owner.CompleteOperation(this);
            var onDisposed = Interlocked.Exchange(ref _onDisposed, null);
            onDisposed?.Invoke();
        }
    }
}
