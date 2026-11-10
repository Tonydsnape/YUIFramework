using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace YUIFramework
{
    public interface IUISystemClock
    {
        double Seconds { get; }
    }

    public sealed class UnityUISystemClock : IUISystemClock
    {
        public double Seconds => Time.unscaledTimeAsDouble;
    }

    public enum UIToastOutcome
    {
        Displayed, Merged, RateLimited, Overflow, Superseded, Canceled, ServiceStopped
    }

    public sealed class UIToastRequest
    {
        public UIToastRequest(string key, string text, double duration = 2, int priority = 0)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("A merge key is required.", nameof(key));
            if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Toast text is required.", nameof(text));
            if (double.IsNaN(duration) || double.IsInfinity(duration) || duration <= 0)
                throw new ArgumentOutOfRangeException(nameof(duration));
            Key = key; Text = text; Duration = duration; Priority = priority;
        }
        public string Key { get; }
        public string Text { get; }
        public double Duration { get; }
        public int Priority { get; }
    }

    public sealed class UIToastItem
    {
        internal UIToastItem(UIToastRequest request, UIScope owner, UIToastTicket ticket)
        {
            Request = request; Owner = owner; Ticket = ticket;
        }
        public UIToastRequest Request { get; }
        public int Count { get; internal set; } = 1;
        internal UIScope Owner { get; }
        internal UIToastTicket Ticket { get; }
    }

    public sealed class UIToastTicket : IDisposable
    {
        private readonly UniTaskCompletionSource<UIToastOutcome> _completion = new UniTaskCompletionSource<UIToastOutcome>();
        private Action _cancel;
        private CancellationTokenRegistration _registration;
        public UniTask<UIToastOutcome> Completion => _completion.Task;
        public bool IsCompleted { get; private set; }
        internal void Attach(CancellationToken token, Action cancel)
        {
            _cancel = cancel;
            var registration = token.Register(Dispose);
            if (IsCompleted) registration.Dispose();
            else _registration = registration;
        }
        internal void Complete(UIToastOutcome outcome)
        {
            _cancel = null;
            _registration.Dispose();
            IsCompleted = true;
            _completion.TrySetResult(outcome);
        }
        public void Dispose() => _cancel?.Invoke();
    }

    public sealed class UIToastService : IDisposable
    {
        private readonly IUISystemClock _clock;
        private readonly int _capacity, _maxPerWindow;
        private readonly double _window, _minimumInterval;
        private readonly List<UIToastItem> _pending = new List<UIToastItem>();
        private readonly Queue<double> _admissions = new Queue<double>();
        private double _deadline, _nextStart;
        private bool _disposed;
        public UIToastService(IUISystemClock clock = null, int capacity = 8, int maxPerWindow = 10,
            double windowSeconds = 1, double minimumInterval = .1)
        {
            if (capacity <= 0 || maxPerWindow <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (!Finite(windowSeconds) || windowSeconds <= 0 || !Finite(minimumInterval) || minimumInterval < 0)
                throw new ArgumentOutOfRangeException(nameof(windowSeconds));
            _clock = clock ?? new UnityUISystemClock();
            _capacity = capacity; _maxPerWindow = maxPerWindow;
            _window = windowSeconds; _minimumInterval = minimumInterval;
        }
        public UIToastItem Current { get; private set; }
        public int Count => _pending.Count + (Current == null ? 0 : 1);
        public event Action<UIToastItem> Changed;

        public UIToastTicket Enqueue(UIScope owner, UIToastRequest request)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UIToastService));
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (owner.IsEnded) throw new ObjectDisposedException(nameof(owner));
            var ticket = new UIToastTicket();
            var merged = Matches(Current, owner, request) ? Current :
                _pending.Find(item => Matches(item, owner, request));
            if (merged != null)
            {
                if (merged.Count < int.MaxValue) merged.Count++;
                ticket.Complete(UIToastOutcome.Merged);
                if (ReferenceEquals(Current, merged)) UISystemEvents.Publish(Changed, Current);
                return ticket;
            }
            var now = _clock.Seconds;
            while (_admissions.Count > 0 && now - _admissions.Peek() >= _window) _admissions.Dequeue();
            if (_admissions.Count >= _maxPerWindow)
            {
                ticket.Complete(UIToastOutcome.RateLimited);
                return ticket;
            }
            if (Count >= _capacity)
            {
                var lowest = -1;
                for (var i = 0; i < _pending.Count; i++)
                    if (lowest < 0 || _pending[i].Request.Priority < _pending[lowest].Request.Priority) lowest = i;
                if (lowest < 0 || _pending[lowest].Request.Priority >= request.Priority)
                {
                    ticket.Complete(UIToastOutcome.Overflow);
                    return ticket;
                }
                var evicted = _pending[lowest];
                _pending.RemoveAt(lowest);
                evicted.Ticket.Complete(UIToastOutcome.Superseded);
            }
            var item = new UIToastItem(request, owner, ticket);
            _pending.Add(item);
            _admissions.Enqueue(now);
            ticket.Attach(owner.Token, () => Cancel(item));
            return ticket;
        }

        public void Tick()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UIToastService));
            var now = _clock.Seconds;
            if (Current != null && now >= _deadline)
            {
                var finished = Current;
                Current = null;
                _nextStart = now + _minimumInterval;
                finished.Ticket.Complete(UIToastOutcome.Displayed);
                UISystemEvents.Publish(Changed, Current);
            }
            if (Current != null || _pending.Count == 0 || now < _nextStart) return;
            var best = 0;
            for (var i = 1; i < _pending.Count; i++)
                if (_pending[i].Request.Priority > _pending[best].Request.Priority) best = i;
            Current = _pending[best];
            _pending.RemoveAt(best);
            _deadline = now + Current.Request.Duration;
            UISystemEvents.Publish(Changed, Current);
        }

        private void Cancel(UIToastItem item)
        {
            if (ReferenceEquals(item, Current))
            {
                Current = null;
                _nextStart = _clock.Seconds + _minimumInterval;
                item.Ticket.Complete(UIToastOutcome.Canceled);
                UISystemEvents.Publish(Changed, Current);
            }
            else if (_pending.Remove(item)) item.Ticket.Complete(UIToastOutcome.Canceled);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            var current = Current;
            Current = null;
            foreach (var item in _pending) item.Ticket.Complete(UIToastOutcome.ServiceStopped);
            _pending.Clear();
            _admissions.Clear();
            current?.Ticket.Complete(UIToastOutcome.ServiceStopped);
            try { UISystemEvents.Publish(Changed, Current); }
            finally { Changed = null; }
        }

        private static bool Matches(UIToastItem item, UIScope owner, UIToastRequest request) =>
            item != null && ReferenceEquals(item.Owner, owner) &&
            string.Equals(item.Request.Key, request.Key, StringComparison.Ordinal);
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
