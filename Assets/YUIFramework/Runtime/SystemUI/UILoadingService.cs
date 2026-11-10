using System;
using System.Collections.Generic;
using System.Threading;

namespace YUIFramework
{
    public readonly struct UILoadingState
    {
        public UILoadingState(int count, float? progress, string message, bool canCancel)
        {
            Count = count;
            Progress = progress;
            Message = message;
            CanCancel = canCancel;
        }
        public int Count { get; }
        public bool IsVisible => Count > 0;
        public float? Progress { get; }
        public string Message { get; }
        public bool CanCancel { get; }
    }

    public sealed class UILoadingService : IDisposable
    {
        private readonly List<UILoadingOperation> _operations = new List<UILoadingOperation>();
        private bool _disposed;
        public event Action<UILoadingState> Changed;
        public UILoadingState State { get; private set; }

        public UILoadingOperation Begin(UIScope owner, string message, bool canCancel = true,
            float weight = 1, CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UILoadingService));
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (owner.IsEnded) throw new ObjectDisposedException(nameof(owner));
            if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("A loading message is required.", nameof(message));
            if (float.IsNaN(weight) || float.IsInfinity(weight) || weight <= 0)
                throw new ArgumentOutOfRangeException(nameof(weight));
            cancellationToken.ThrowIfCancellationRequested();
            var operation = new UILoadingOperation(this, owner, message, canCancel, weight, cancellationToken);
            _operations.Add(operation);
            try
            {
                operation.Attach();
                Refresh();
                return operation;
            }
            catch (Exception original)
            {
                try { operation.Dispose(); }
                catch (Exception cleanup) { throw new AggregateException(original, cleanup); }
                throw;
            }
        }

        public void CancelCancelable()
        {
            var errors = new List<Exception>();
            foreach (var operation in _operations.ToArray())
            {
                if (!operation.CanCancel) continue;
                try { operation.Cancel(); }
                catch (Exception exception) { errors.Add(exception); }
            }
            if (errors.Count > 0) throw new AggregateException("Loading cancellation failed.", errors);
        }

        internal void Remove(UILoadingOperation operation)
        {
            if (_operations.Remove(operation)) Refresh();
        }

        internal void Refresh()
        {
            double totalWeight = 0, progress = 0;
            var determinate = true;
            var canCancel = false;
            foreach (var operation in _operations)
            {
                totalWeight += operation.Weight;
                if (operation.Progress.HasValue) progress += operation.Progress.Value * operation.Weight;
                else determinate = false;
                canCancel |= operation.CanCancel;
            }
            State = new UILoadingState(_operations.Count,
                determinate && totalWeight > 0 ? (float?)(progress / totalWeight) : null,
                _operations.Count == 0 ? string.Empty : _operations[_operations.Count - 1].Message, canCancel);
            UISystemEvents.Publish(Changed, State);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            var errors = new List<Exception>();
            foreach (var operation in _operations.ToArray())
            {
                try { operation.Dispose(); }
                catch (Exception exception) { errors.Add(exception); }
            }
            Changed = null;
            if (errors.Count > 0) throw new AggregateException("Loading teardown failed.", errors);
        }
    }

    public sealed class UILoadingOperation : IDisposable, IProgress<float>
    {
        private UILoadingService _service;
        private readonly CancellationTokenSource _cancellation;
        private CancellationTokenRegistration _registration;
        private bool _ended;

        internal UILoadingOperation(UILoadingService service, UIScope owner, string message,
            bool canCancel, float weight, CancellationToken caller)
        {
            _service = service;
            Message = message;
            CanCancel = canCancel;
            Weight = weight;
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(owner.Token, caller);
            Token = _cancellation.Token;
        }
        public CancellationToken Token { get; }
        public string Message { get; }
        public bool CanCancel { get; }
        public float Weight { get; }
        public float? Progress { get; private set; }
        public bool IsEnded => _ended;

        internal void Attach()
        {
            var registration = Token.Register(Dispose);
            if (_ended) registration.Dispose();
            else _registration = registration;
        }

        public void Report(float value)
        {
            if (_ended) return;
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0 || value > 1)
                throw new ArgumentOutOfRangeException(nameof(value));
            if (Progress == value) return;
            Progress = value;
            _service.Refresh();
        }

        public void Cancel()
        {
            if (!CanCancel) throw new InvalidOperationException("This operation does not allow user cancellation.");
            Dispose();
        }

        public void Dispose()
        {
            if (_ended) return;
            _ended = true;
            var service = _service;
            _service = null;
            _registration.Dispose();
            try
            {
                try { _cancellation.Cancel(); }
                finally { service.Remove(this); }
            }
            finally { _cancellation.Dispose(); }
        }
    }
}
