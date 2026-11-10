using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace YUIFramework
{
    public partial class UIManager
    {
        internal CancellationToken GetNavigationScopeToken(UIPoolScope scope) =>
            GetScopeServiceToken(scope);

        internal bool IsContextInScope(BaseContext context, UIPoolScope scope) =>
            ResolveContextScope(context) == NormalizeScope(scope);

        internal bool IsContextScopeEnding(BaseContext context) => Scopes.IsEnding(ResolveContextScope(context));

        internal void ValidateNavigationScope<T>(UIPoolScope scope) where T : BaseContext
        {
            EnsureScopeAlive(NormalizeScope(scope));
            if (_activeContexts.TryGetValue(typeof(T), out var context) &&
                !IsContextInScope(context, scope) && !Scopes.IsEnding(ResolveContextScope(context)))
            {
                throw new InvalidOperationException(
                    $"{typeof(T).Name} already belongs to a different scope.");
            }
        }

        internal void CancelUIScope(UIPoolScope scope)
        {
            try
            {
                if (_poolScopes.TryGetValue(scope.Id, out var state)) state.End();
            }
            finally
            {
                var handles = new List<int>();
                foreach (var pair in _sceneScopes)
                {
                    if (pair.Value == scope) handles.Add(pair.Key);
                }
                foreach (var handle in handles) _sceneScopes.Remove(handle);
            }
        }

        internal async UniTask ReleaseUIScopeAsync(UIPoolScope scope)
        {
            var errors = new List<Exception>();
            try { await Navigator.RemoveScopeAsync(scope); }
            catch (Exception exception) { AddFlattened(errors, exception); }

            var types = new List<Type>(_configRegistry.Keys);
            foreach (var type in types)
            {
                try
                {
                    await _coordinator.EnqueueAsync(type, "EndUIScope", async _ =>
                    {
                        if (_activeContexts.TryGetValue(type, out var context) &&
                            IsContextInScope(context, scope))
                        {
                            await CloseInternalAsync(context, CancellationToken.None, true);
                        }
                    }, CancellationToken.None);
                }
                catch (Exception exception) { AddFlattened(errors, exception); }
            }
            try { _objectPool.ClearScope(scope, pooled => DestroyPooledObject(pooled, true)); }
            catch (Exception exception) { AddFlattened(errors, exception); }
            if (_poolScopes.TryGetValue(scope.Id, out var state))
            {
                _poolScopes.Remove(scope.Id);
                try { state.Dispose(); }
                catch (Exception exception) { AddFlattened(errors, exception); }
            }
            if (errors.Count > 0) throw new AggregateException($"UI scope cleanup failed: {scope}.", errors);
        }
    }
}
