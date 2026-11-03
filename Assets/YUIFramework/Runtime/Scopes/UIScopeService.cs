using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace YUIFramework
{
    public sealed class UIScopeService
    {
        private readonly UIManager _ui;
        private readonly Dictionary<UIPoolScope, UIScope> _scopes = new Dictionary<UIPoolScope, UIScope>();
        private readonly Dictionary<int, UIScope> _scenes = new Dictionary<int, UIScope>();
        private readonly List<Task> _ending = new List<Task>();
        private readonly Dictionary<UIPoolScope, UIScope> _endingScopes = new Dictionary<UIPoolScope, UIScope>();
        private bool _stopped;

        internal UIScopeService(UIManager ui)
        {
            _ui = ui;
            Global = Add(UIPoolScope.Global);
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
        }

        public UIScope Global { get; }
        public UIScope CurrentScene => ForScene(SceneManager.GetActiveScene());

        public UIScope ForScene(Scene scene)
        {
            EnsureRunning();
            if (!scene.IsValid() || !scene.isLoaded)
                throw new ArgumentException("A loaded scene is required.", nameof(scene));
            if (_scenes.TryGetValue(scene.handle, out var scope)) return scope;
            scope = Add(_ui.GetSceneScope(scene));
            _scenes.Add(scene.handle, scope);
            return scope;
        }

        public UIScope CreateModule(string name)
        {
            EnsureRunning();
            return Add(_ui.CreateModuleScope(name));
        }

        private UIScope Add(UIPoolScope identity)
        {
            var scope = new UIScope(this, _ui, identity);
            _scopes.Add(identity, scope);
            return scope;
        }

        internal UniTask EndAsync(UIScope scope)
        {
            if (scope == Global)
                throw new InvalidOperationException("The global UI scope ends only with UI shutdown.");
            if (scope.EndTask != null) return scope.EndTask.AsUniTask();

            var completion = new TaskCompletionSource<object>();
            scope.EndTask = completion.Task;
            _ending.Add(completion.Task);
            _endingScopes.Add(scope.Identity, scope);
            _scopes.Remove(scope.Identity);
            var handles = new List<int>();
            foreach (var pair in _scenes)
                if (ReferenceEquals(pair.Value, scope)) handles.Add(pair.Key);
            foreach (var handle in handles) _scenes.Remove(handle);
            CompleteEndAsync(scope, completion).Forget(Debug.LogException);
            return completion.Task.AsUniTask();
        }

        private async UniTask CompleteEndAsync(UIScope scope, TaskCompletionSource<object> completion)
        {
            var errors = new List<Exception>();
            try { scope.Cancel(); }
            catch (Exception exception) { errors.Add(exception); }
            try { _ui.CancelUIScope(scope.Identity); }
            catch (Exception exception) { errors.Add(exception); }
            try { await _ui.ReleaseUIScopeAsync(scope.Identity); }
            catch (Exception exception) { errors.Add(exception); }
            try { await scope.DrainAsync(); }
            catch (Exception exception) { errors.Add(exception); }
            scope.DisposeCancellation();
            if (errors.Count == 0) completion.TrySetResult(null);
            else completion.TrySetException(new AggregateException("UI scope teardown failed.", errors));
            // EndTask retains a shared result for repeated callers; the service need not retain completed scopes.
            _ending.Remove(completion.Task);
            _endingScopes.Remove(scope.Identity);
        }

        internal bool IsEnding(UIPoolScope identity) => _endingScopes.ContainsKey(identity);
        internal UIScope Find(UIPoolScope identity) =>
            _scopes.TryGetValue(identity, out var scope) || _endingScopes.TryGetValue(identity, out scope)
                ? scope : null;

        internal IReadOnlyList<UIScope> GetDiagnosticScopes()
        {
            var scopes = new List<UIScope>(_scopes.Values);
            scopes.AddRange(_endingScopes.Values);
            return scopes.AsReadOnly();
        }

        internal bool TryGetToken(UIPoolScope identity, out CancellationToken token)
        {
            if (_scopes.TryGetValue(identity, out var scope) || _endingScopes.TryGetValue(identity, out scope))
            {
                token = scope.Token;
                return true;
            }
            token = default;
            return false;
        }

        internal bool TryEndScene(int handle)
        {
            if (_stopped || !_scenes.TryGetValue(handle, out var scope)) return false;
            EndAsync(scope).Forget(Debug.LogException);
            return true;
        }

        private void OnActiveSceneChanged(Scene previous, Scene next) => TryEndScene(previous.handle);

        internal async UniTask StopAsync()
        {
            if (_stopped) return;
            _stopped = true;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            var errors = new List<Exception>();
            try { Global.Cancel(); }
            catch (Exception exception) { errors.Add(exception); }
            var pending = new List<Task>(_ending);
            foreach (var scope in new List<UIScope>(_scopes.Values))
            {
                if (scope == Global) continue;
                pending.Add(EndAsync(scope).AsTask());
            }
            foreach (var task in pending)
            {
                try { await task.AsUniTask(); }
                catch (Exception exception) { errors.Add(exception); }
            }
            await Global.DrainAsync();
            Global.DisposeCancellation();
            _scopes.Clear();
            _scenes.Clear();
            if (errors.Count > 0) throw new AggregateException("UI scopes failed to stop.", errors);
        }

        private void EnsureRunning()
        {
            if (_stopped) throw new ObjectDisposedException(nameof(UIScopeService));
        }
    }

    public sealed class UIScope
    {
        private readonly UIScopeService _owner;
        private readonly UIManager _ui;
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private int _operations;

        internal UIScope(UIScopeService owner, UIManager ui, UIPoolScope identity)
        {
            _owner = owner;
            _ui = ui;
            Identity = identity;
            Token = _cancellation.Token;
        }

        public UIPoolScope Identity { get; }
        public CancellationToken Token { get; }
        public bool IsEnded { get; private set; }
        public int OperationCount => _operations;
        internal Task EndTask { get; set; }

        public UniTask<T> OpenAsync<T>(object args = null, CancellationToken cancellationToken = default)
            where T : BaseContext => RunAsync(ct => _ui.OpenInScopeAsync<T>(Identity, args, ct), cancellationToken);

        public UniTask<T> PushAsync<T>(object args = null, CancellationToken cancellationToken = default)
            where T : BasePageContext => RunAsync(ct => _ui.Navigator.PushAsync<T>(
                args, new UINavigateOptions { Scope = Identity }, ct), cancellationToken);

        public async UniTask<T> RunAsync<T>(
            Func<CancellationToken, UniTask<T>> operation, CancellationToken cancellationToken = default)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            if (IsEnded) throw new ObjectDisposedException(nameof(UIScope));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(Token, cancellationToken);
            linked.Token.ThrowIfCancellationRequested();
            _operations++;
            using var diagnostic = _ui.Diagnostics?.Begin("Task", scope: this, token: linked.Token);
            try
            {
                var result = await operation(linked.Token);
                linked.Token.ThrowIfCancellationRequested();
                return result;
            }
            catch (Exception exception) { diagnostic?.Fail(exception); throw; }
            finally { _operations--; }
        }

        public async UniTask RunAsync(
            Func<CancellationToken, UniTask> operation, CancellationToken cancellationToken = default)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            await RunAsync(async ct => { await operation(ct); return true; }, cancellationToken);
        }

        public UniTask ReleaseAsync() => _owner.EndAsync(this);

        internal void Cancel()
        {
            IsEnded = true;
            _cancellation.Cancel();
        }

        internal async UniTask DrainAsync()
        {
            while (_operations > 0) await UniTask.Yield();
        }

        internal void DisposeCancellation() => _cancellation.Dispose();
    }
}
