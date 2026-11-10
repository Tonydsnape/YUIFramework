using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

namespace YUIFramework
{
    public interface IUIDiagnosticClock
    {
        double Seconds { get; }
        int Frame { get; }
    }

    public sealed class StopwatchUIDiagnosticClock : IUIDiagnosticClock
    {
        public double Seconds => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        public int Frame => Time.frameCount;
    }

    public interface IUIDiagnosticsAdapter
    {
        void Write(UIDiagnosticEvent entry);
    }

    public enum UIDiagnosticOutcome { Begin, Phase, Completed, Canceled, Faulted, AdapterFault }

    public sealed class UIDiagnosticEvent
    {
        internal UIDiagnosticEvent(long sequence, UIOperationId id, UIOperationId parent,
            string kind, string page, long instance, string scope, string resource, string phase,
            UIDiagnosticOutcome outcome, double seconds, double durationMs, int frame, string error)
        {
            Sequence = sequence; OperationId = id; ParentId = parent; Kind = kind;
            Page = page; InstanceId = instance; Scope = scope; Resource = resource;
            Phase = phase; Outcome = outcome; Seconds = seconds; DurationMs = durationMs;
            Frame = frame; ExceptionChain = error;
        }
        public long Sequence { get; }
        public UIOperationId OperationId { get; }
        public UIOperationId ParentId { get; }
        public string Kind { get; }
        public string Page { get; }
        public long InstanceId { get; }
        public string Scope { get; }
        public string Resource { get; }
        public string Phase { get; }
        public UIDiagnosticOutcome Outcome { get; }
        public double Seconds { get; }
        public double DurationMs { get; }
        public int Frame { get; }
        public string ExceptionChain { get; }
        public override string ToString() =>
            $"#{Sequence} op={OperationId} parent={ParentId} {Kind}/{Phase} {Outcome} " +
            $"page={Page} instance={InstanceId} scope={Scope} address={Resource} " +
            $"durationMs={DurationMs:F3} frame={Frame} {ExceptionChain}";
    }

    public sealed class UIDiagnosticOperationSnapshot
    {
        internal UIDiagnosticOperationSnapshot(UIDiagnosticSpan span, double now)
        {
            OperationId = span.Id; ParentId = span.ParentId; Kind = span.Kind;
            Page = span.Page; InstanceId = span.InstanceId; Scope = span.Scope;
            Resource = span.Resource; Phase = span.Phase;
            ElapsedMs = Math.Max(0, now - span.StartSeconds) * 1000;
            CancellationRequested = span.Token.IsCancellationRequested;
            OwnerEnded = span.OwnerEnded;
        }
        public UIOperationId OperationId { get; }
        public UIOperationId ParentId { get; }
        public string Kind { get; }
        public string Page { get; }
        public long InstanceId { get; }
        public string Scope { get; }
        public string Resource { get; }
        public string Phase { get; }
        public double ElapsedMs { get; }
        public bool CancellationRequested { get; }
        public bool OwnerEnded { get; }
    }

    public sealed class UIDiagnosticSpan : IDisposable
    {
        private UIDiagnostics _service;
        private readonly WeakReference<BaseContext> _owner;
        private readonly WeakReference<UIScope> _scope;
        private UIDiagnosticOutcome _outcome = UIDiagnosticOutcome.Completed;
        private string _error = "";
        internal UIDiagnosticSpan(UIDiagnostics service, UIOperationId id, UIOperationId parent,
            string kind, BaseContext owner, UIScope scope, string page, string resource,
            string phase, CancellationToken token, double seconds, int frame)
        {
            _service = service; Id = id; ParentId = parent; Kind = kind;
            Page = page ?? owner?.GetType().Name ?? ""; InstanceId = owner?.DiagnosticInstanceId ?? 0;
            Scope = scope?.Identity.ToString() ?? owner?.DiagnosticScope ?? "";
            Resource = resource ?? ""; Phase = phase ?? "";
            Token = token; StartSeconds = seconds; StartFrame = frame;
            if (owner != null) _owner = new WeakReference<BaseContext>(owner);
            if (scope != null) _scope = new WeakReference<UIScope>(scope);
        }
        public UIOperationId Id { get; }
        public UIOperationId ParentId { get; }
        public string Kind { get; }
        public string Page { get; }
        public long InstanceId { get; }
        public string Scope { get; }
        public string Resource { get; }
        public string Phase { get; private set; }
        public double StartSeconds { get; }
        public int StartFrame { get; }
        internal CancellationToken Token { get; }
        internal bool InferTokenCancellation { get; set; } = true;
        internal bool OwnerEnded =>
            (_owner != null && (!_owner.TryGetTarget(out var owner) || owner.LifetimeToken.IsCancellationRequested)) ||
            (_scope != null && (!_scope.TryGetTarget(out var scope) || scope.IsEnded));
        internal BaseContext Owner => _owner != null && _owner.TryGetTarget(out var owner) ? owner : null;
        public void Mark(string phase)
        {
            Phase = phase ?? "";
            _service?.Record(this, UIDiagnosticOutcome.Phase, "");
        }
        public void Fail(Exception exception)
        {
            if (exception == null) return;
            _outcome = exception is OperationCanceledException
                ? UIDiagnosticOutcome.Canceled : UIDiagnosticOutcome.Faulted;
            _error = UIDiagnostics.ExceptionText(exception);
        }
        public void Dispose()
        {
            var service = _service;
            _service = null;
            if (service == null) return;
            if (_outcome == UIDiagnosticOutcome.Completed && InferTokenCancellation && Token.IsCancellationRequested)
                _outcome = UIDiagnosticOutcome.Canceled;
            service.End(this, _outcome, _error);
        }
    }

    public sealed partial class UIDiagnostics : IDisposable
    {
        private readonly IUIDiagnosticClock _clock;
        private readonly UIDiagnosticEvent[] _events;
        private readonly Dictionary<UIOperationId, UIDiagnosticSpan> _active =
            new Dictionary<UIOperationId, UIDiagnosticSpan>();
        private readonly List<IUIDiagnosticsAdapter> _adapters = new List<IUIDiagnosticsAdapter>();
        private readonly int _activeCapacity;
        private int _next, _count;
        private long _sequence;
        private long _adapterCursor;
        private double _lastSeconds;
        private int _lastFrame;
        private bool _disposed, _dispatching;
        public UIDiagnostics(int eventCapacity = 512, int activeCapacity = 128,
            IUIDiagnosticClock clock = null)
        {
            if (eventCapacity < 1 || activeCapacity < 1) throw new ArgumentOutOfRangeException(nameof(eventCapacity));
            _events = new UIDiagnosticEvent[eventCapacity]; _activeCapacity = activeCapacity;
            _clock = clock ?? new StopwatchUIDiagnosticClock();
        }
        public bool Enabled { get; set; } = true;
        public int EventCapacity => _events.Length;
        public int ActiveCount => _active.Count;
        public long DroppedEvents { get; private set; }
        public long DroppedOperations { get; private set; }
        public long AdapterFaultCount { get; private set; }
        public long ClockFaultCount { get; private set; }
        public int AdapterCount => _adapters.Count;

        public IDisposable Attach(IUIDiagnosticsAdapter adapter)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UIDiagnostics));
            if (adapter == null) throw new ArgumentNullException(nameof(adapter));
            if (_adapters.Contains(adapter)) throw new InvalidOperationException("The adapter is already attached.");
            _adapters.Add(adapter);
            return new BindingToken(() => _adapters.Remove(adapter));
        }

        public UIDiagnosticSpan Begin(string kind, BaseContext owner = null, UIScope scope = null,
            UIOperationId parent = default, string page = null, string resource = null,
            string phase = "execute", CancellationToken token = default, UIOperationId id = default)
        {
            if (!Enabled || _disposed) return null;
            if (_dispatching) { DroppedOperations++; return null; }
            if (_active.Count >= _activeCapacity) { DroppedOperations++; return null; }
            if (!ReadClock()) return null;
            if (!id.IsValid) id = UIOperationId.Next();
            if (_active.ContainsKey(id)) { DroppedOperations++; return null; }
            var span = new UIDiagnosticSpan(this, id, parent, kind, owner, scope, page, resource,
                phase, token, _lastSeconds, _lastFrame);
            _active.Add(id, span);
            Record(span, UIDiagnosticOutcome.Begin, "");
            return span;
        }

        public IReadOnlyList<UIDiagnosticEvent> GetEvents()
        {
            var copy = new UIDiagnosticEvent[_count];
            for (var i = 0; i < _count; i++)
                copy[i] = _events[(_next - _count + i + _events.Length) % _events.Length];
            return Array.AsReadOnly(copy);
        }

        public IReadOnlyList<UIDiagnosticOperationSnapshot> GetOperations()
        {
            ReadClock();
            var copy = new List<UIDiagnosticOperationSnapshot>(_active.Count);
            foreach (var span in _active.Values) copy.Add(new UIDiagnosticOperationSnapshot(span, _lastSeconds));
            return copy.AsReadOnly();
        }

        internal void Record(UIDiagnosticSpan span, UIDiagnosticOutcome outcome, string error)
        {
            ReadClock();
            Write(new UIDiagnosticEvent(++_sequence, span.Id, span.ParentId, span.Kind,
                span.Page, span.InstanceId, span.Scope, span.Resource, span.Phase, outcome,
                _lastSeconds, Math.Max(0, _lastSeconds - span.StartSeconds) * 1000, _lastFrame, error));
        }

        internal void End(UIDiagnosticSpan span, UIDiagnosticOutcome outcome, string error)
        {
            _active.Remove(span.Id);
            Record(span, outcome, error);
        }

        private bool ReadClock()
        {
            try
            {
                var now = _clock.Seconds;
                var frame = _clock.Frame;
                if (double.IsNaN(now) || double.IsInfinity(now) || now < _lastSeconds)
                    throw new InvalidOperationException("Diagnostic clock must be finite and monotonic.");
                _lastSeconds = now; _lastFrame = frame;
                return true;
            }
            catch (Exception exception)
            {
                ClockFaultCount++;
                if (ClockFaultCount == 1) UnityEngine.Debug.LogWarning(
                    "[UIDiagnostics] Clock failure; measurements are incomplete. " + ExceptionText(exception));
                return false;
            }
        }

        private void Write(UIDiagnosticEvent entry)
        {
            if (_count == _events.Length) DroppedEvents++; else _count++;
            _events[_next] = entry; _next = (_next + 1) % _events.Length;
        }

        public void FlushAdapters()
        {
            if (_disposed || _dispatching || _adapters.Count == 0) return;
            var pending = GetEvents();
            var through = _sequence;
            _dispatching = true;
            try
            {
                foreach (var adapter in _adapters.ToArray())
                {
                    if (!_adapters.Contains(adapter)) continue;
                    foreach (var entry in pending)
                    {
                        if (entry.Sequence <= _adapterCursor) continue;
                        try { adapter.Write(entry); }
                        catch (Exception exception)
                        {
                            _adapters.Remove(adapter);
                            AdapterFaultCount++;
                            Write(new UIDiagnosticEvent(++_sequence, entry.OperationId, entry.ParentId,
                                "Diagnostics", entry.Page, entry.InstanceId, entry.Scope, entry.Resource,
                                "adapter-quarantined", UIDiagnosticOutcome.AdapterFault, _lastSeconds, 0,
                                _lastFrame, ExceptionText(exception)));
                            UnityEngine.Debug.LogWarning("[UIDiagnostics] Adapter quarantined: " + ExceptionText(exception));
                            break;
                        }
                        if (!_adapters.Contains(adapter)) break;
                    }
                }
            }
            finally { _adapterCursor = through; _dispatching = false; }
        }

        internal static string ExceptionText(Exception exception)
        {
            string text;
            try { text = exception.ToString() ?? exception.GetType().FullName; }
            catch (Exception formattingError)
            {
                text = exception.GetType().FullName + " [exception formatter failed: " +
                    formattingError.GetType().FullName + "]";
            }

            return text.Length <= 8192 ? text : text.Substring(0, 8192) + " [truncated]";
        }

        internal static string StateName(UIContextState state) =>
            state == UIContextState.Unloaded ? "Unloaded" : state == UIContextState.Opened ? "Opened" :
            state == UIContextState.Pooled ? "Pooled" : state == UIContextState.Released ? "Released" : state.ToString();

        public void Dispose()
        {
            _disposed = true; Enabled = false; _adapters.Clear();
            // Running work still owns its spans; disposal never fabricates a completed operation.
        }
    }
}
