using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace YUIFramework
{
    public partial class UIManager
    {
        private async UniTask<IUIInstanceLease> InstantiateContextAsync(
            BaseContext context, UIConfig config, UIPoolScope scope, CancellationToken token)
        {
            var key = UIResourceKey.Of<GameObject>(config.PrefabKey, config.PrefabPackage);
            if (_resourceService is UIResourceService traced && ReferenceEquals(traced.Diagnostics, Diagnostics))
                return await traced.InstantiateDiagnosticAsync(key, null, token,
                    context.DiagnosticOperation, context, Scopes.Find(scope));
            using var span = Diagnostics?.Begin("Load", context, Scopes.Find(scope),
                context.CurrentOperationId, resource: key.ToString(), token: token);
            try { return await _resourceService.InstantiateAsync(key, null, token); }
            catch (Exception exception) { span?.Fail(exception); throw; }
        }

        private async UniTask<GameObject> LoadContextPrefabAsync(
            BaseContext context, UIConfig config, UIPoolScope scope, CancellationToken token)
        {
            using var span = Diagnostics?.Begin("Load", context, Scopes.Find(scope),
                context.CurrentOperationId, resource: config.PrefabKey, phase: "legacy-minimal-loader", token: token);
            try { return await _resourceLoader.LoadPrefabAsync(config.PrefabKey, token); }
            catch (Exception exception) { span?.Fail(exception); throw; }
        }

        internal IReadOnlyList<BaseContext> GetDiagnosticContexts()
        {
            var contexts = new HashSet<BaseContext>(_activeContexts.Values);
            contexts.UnionWith(_contextInstanceLeases.Keys);
            contexts.UnionWith(_contextScopes.Keys);
            contexts.UnionWith(_transitioningContexts.Values);
            return new List<BaseContext>(contexts).AsReadOnly();
        }

        internal UIContextDiagnosticSnapshot CaptureContext(BaseContext context)
        {
            _configRegistry.TryGetValue(context.GetType(), out var config);
            return new UIContextDiagnosticSnapshot(context, ResolveContextScope(context),
                _contextPrefabKeys.TryGetValue(context, out var address) ? address : config?.PrefabKey ?? "",
                _contextInstanceLeases.TryGetValue(context, out var lease) && !lease.IsReleased, config?.Id);
        }
    }
}
