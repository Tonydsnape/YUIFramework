using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace YUIFramework.Configuration
{
    // Main-thread confined. Individual callers cancel their wait, not the shared transaction.
    public sealed class ConfigService
    {
        private readonly IConfigSource _source;
        private readonly IConfigCodec _codec;
        private readonly ConfigFormat _format;
        private readonly ConfigTable[] _tables;
        private readonly int _thread = Thread.CurrentThread.ManagedThreadId;
        private Run _running;
        private long _generation;
        private UniTaskCompletionSource _shutdown;
        public ConfigSnapshot Snapshot { get; private set; }
        public Exception LastFailure { get; private set; }

        public ConfigService(IConfigSource source, IConfigCodec codec, ConfigFormat format,
            IEnumerable<ConfigTable> tables)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _codec = codec ?? throw new ArgumentNullException(nameof(codec));
            if (!Enum.IsDefined(typeof(ConfigFormat), format)) throw new ArgumentOutOfRangeException(nameof(format));
            _format = format;
            var copy = new List<ConfigTable>(tables ?? throw new ArgumentNullException(nameof(tables)));
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var table in copy)
                if (table == null || !names.Add(table.Name)) throw new ArgumentException("Duplicate/null config table.");
            if (copy.Count == 0) throw new ArgumentException("At least one config table is required.");
            _tables = copy.ToArray();
        }

        public UniTask<ConfigSnapshot> InitializeAsync(CancellationToken cancellationToken = default)
        {
            CheckThread();
            cancellationToken.ThrowIfCancellationRequested();
            if (_shutdown != null) throw new InvalidOperationException("Config shutdown is still draining.");
            if (Snapshot != null) return UniTask.FromResult(Snapshot);
            return LoadAsync(cancellationToken);
        }
        public UniTask<ConfigSnapshot> ReloadAsync(CancellationToken cancellationToken = default)
        {
            CheckThread();
            cancellationToken.ThrowIfCancellationRequested();
            if (_shutdown != null) throw new InvalidOperationException("Config shutdown is still draining.");
            return LoadAsync(cancellationToken);
        }
        private UniTask<ConfigSnapshot> LoadAsync(CancellationToken token)
        {
            if (_running == null)
            {
                var run = new Run(++_generation);
                _running = run;
                ExecuteAsync(run).Forget(UnityEngine.Debug.LogException);
                return WaitAsync(run, token);
            }
            return WaitAsync(_running, token);
        }
        private static async UniTask<ConfigSnapshot> WaitAsync(Run run, CancellationToken token)
        {
            var result = await run.Completion.Task.AttachExternalCancellation(token);
            token.ThrowIfCancellationRequested();
            if (result.Error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(result.Error).Throw();
            return result.Snapshot;
        }
        private async UniTask ExecuteAsync(Run run)
        {
            ConfigSnapshot snapshot = null;
            Exception failure = null;
            try
            {
                var parsed = new Dictionary<ConfigTable, object>();
                var optional = new List<ConfigTableFailure>();
                foreach (var table in _tables)
                {
                    run.Cancellation.Token.ThrowIfCancellationRequested();
                    try
                    {
                        object value;
                        using (var asset = await _source.LoadAsync(table.Name, run.Cancellation.Token))
                        {
                            run.Cancellation.Token.ThrowIfCancellationRequested();
                            if (asset == null) throw new ConfigDataException($"Missing config asset: {table.Name}");
                            value = table.Parse(_codec.Decode(asset.Bytes, _format));
                        }
                        parsed.Add(table, value);
                    }
                    catch (Exception error) when (!(error is OperationCanceledException))
                    {
                        var contextual = new ConfigDataException($"Unable to load table {table.Name}.", error);
                        if (table.Required) throw contextual;
                        optional.Add(new ConfigTableFailure(table.Name, contextual));
                    }
                }
                CheckThread();
                run.Cancellation.Token.ThrowIfCancellationRequested();
                if (_running != run || _generation != run.Generation)
                    throw new OperationCanceledException("Obsolete config generation.");
                snapshot = new ConfigSnapshot(run.Generation, parsed, optional);
                Snapshot = snapshot;
                LastFailure = null;
            }
            catch (Exception error) { failure = error; LastFailure = error; }
            finally
            {
                if (_running == run) _running = null;
                run.Cancellation.Dispose();
                run.Completion.TrySetResult(new Outcome(snapshot, failure));
            }
        }
        public UniTask ShutdownAsync()
        {
            CheckThread();
            if (_shutdown != null) return _shutdown.Task;
            Snapshot = null;
            _generation++;
            var completion = new UniTaskCompletionSource();
            _shutdown = completion;
            DrainAsync(_running, completion).Forget(UnityEngine.Debug.LogException);
            return completion.Task;
        }
        private async UniTask DrainAsync(Run run, UniTaskCompletionSource completion)
        {
            Exception failure = null;
            try
            {
                if (run != null)
                {
                    try { run.Cancellation.Cancel(); }
                    catch (Exception error) { failure = error; }
                    var outcome = await run.Completion.Task;
                    if (outcome.Error != null && !(outcome.Error is OperationCanceledException))
                        failure = failure == null ? outcome.Error : new AggregateException(failure, outcome.Error);
                }
            }
            finally
            {
                _shutdown = null;
                if (failure == null) completion.TrySetResult();
                else completion.TrySetException(failure);
            }
        }
        private void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != _thread)
                throw new InvalidOperationException("ConfigService must be used on its Unity owning thread.");
        }
        private sealed class Run
        {
            internal Run(long generation) { Generation = generation; }
            internal readonly long Generation;
            internal readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
            internal readonly UniTaskCompletionSource<Outcome> Completion = new UniTaskCompletionSource<Outcome>();
        }
        private readonly struct Outcome
        {
            internal Outcome(ConfigSnapshot snapshot, Exception error) { Snapshot = snapshot; Error = error; }
            internal readonly ConfigSnapshot Snapshot;
            internal readonly Exception Error;
        }
    }
}
