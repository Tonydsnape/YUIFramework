using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Scripting;
using YUIFramework.CodeLoading;

namespace YUIFramework.Hotfix.AotSample
{
    [Preserve]
    public sealed class HotfixBoundarySample : MonoBehaviour
    {
        private readonly UIManager _ui = new UIManager();
        private readonly Views _views = new Views();
        private Task _run = Task.CompletedTask;
        public Exception LastFailure { get; private set; }
        public BaseContext Page { get; private set; }
        private void Start()
        {
            _run = RunAsync(destroyCancellationToken).AsTask();
            _run.AsUniTask().Forget(error => { if (!(error is OperationCanceledException)) Debug.LogException(error); });
        }
        public async UniTask RunAsync(CancellationToken token = default)
        {
            try
            {
                if (_ui.IsInitialized) throw new InvalidOperationException("Sample is already running.");
                _ui.Initialize(_views);
                // Explicit named mock: this assembly is compiled into the base Player.
                // AOT code has no reference to its business types or assembly definition.
                var assembly = Assembly.Load("YUIFramework.Hotfix.Sample");
                var type = assembly.GetType("YUIFramework.Hotfix.Sample.HotfixSampleEntry", true);
                var entry = (IHotfixEntry)Activator.CreateInstance(type);
                Page = await entry.EnterAsync(_ui, new UIConfig { Id = "HotfixSample", PrefabKey = "HotfixSample",
                    Layer = UILayer.Normal, CacheOnClose = true, MaxPoolSize = 1, PreloadCount = 1 }, token);
                await _ui.CloseAsync(Page, token);
                // Removes the first generic-created page; next call exercises the
                // manager's non-generic Activator.CreateInstance(Type) prewarm path.
                await ClearAndPrewarmByPublicContractAsync(token);
                var witness = await HotfixAotWitness.ExerciseAsync(token);
                if (witness != 8) throw new InvalidOperationException("AOT generic witness returned " + witness);
                Debug.Log("[HotfixBoundary] PASS: public entry/open, reflected prewarm and AOT witness.");
                var output = Argument("-yuiHotfixSmoke");
                if (!string.IsNullOrEmpty(output))
                {
                    await _ui.ShutdownAsync();
                    File.WriteAllText(output, "{\"result\":\"Passed\",\"reflectionEntry\":true,\"genericOpen\":true,\"reflectedPrewarm\":true,\"aotWitness\":8,\"cleanup\":true}");
                    Application.Quit(0);
                }
            }
            catch (Exception error)
            {
                LastFailure = error;
                var output = Argument("-yuiHotfixSmoke");
                if (!string.IsNullOrEmpty(output))
                {
                    File.WriteAllText(output, "{\"result\":\"Failed\",\"error\":" + Quote(error.ToString()) + "}");
                    Application.Quit(1);
                }
                throw;
            }
        }
        private async UniTask ClearAndPrewarmByPublicContractAsync(CancellationToken token)
        {
            // No MakeGenericMethod: the AOT owner can use the existing complete
            // registered-prewarm and pool APIs without knowing Hotfix Context types.
            _ui.ClearAllPools();
            if (await _ui.PrewarmRegisteredAsync(cancellationToken: token) != 1)
                throw new InvalidOperationException("Expected one reflected Hotfix prewarm.");
            // Re-entry uses its existing generic UI API inside the Hotfix assembly.
            var assembly = Assembly.Load("YUIFramework.Hotfix.Sample");
            var entry = (IHotfixEntry)Activator.CreateInstance(assembly.GetType("YUIFramework.Hotfix.Sample.HotfixSampleEntry", true));
            Page = await entry.EnterAsync(_ui, new UIConfig { Id = "HotfixSample", PrefabKey = "HotfixSample",
                Layer = UILayer.Normal, CacheOnClose = true, MaxPoolSize = 1, PreloadCount = 1 }, token);
        }
        private void OnDestroy() => CleanupAsync().Forget(Debug.LogException);
        private async UniTask CleanupAsync()
        {
            try { await _run; }
            catch (OperationCanceledException) { }
            catch (Exception error) { Debug.LogException(error); }
            try { await _ui.ShutdownAsync(); }
            finally { _views.Dispose(); }
        }
        private static string Argument(string key)
        {
            var args = Environment.GetCommandLineArgs(); var index = Array.IndexOf(args, key);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
        private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"")
            .Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";
        private sealed class Views : IResourceLoader, IDisposable
        {
            private GameObject _template;
            public UniTask<GameObject> LoadPrefabAsync(string key, CancellationToken token = default)
            {
                token.ThrowIfCancellationRequested();
                if (!_template)
                {
                    _template = new GameObject("Hotfix mock view", typeof(RectTransform), typeof(UIView));
                    _template.SetActive(false); _template.hideFlags = HideFlags.DontSave;
                }
                return UniTask.FromResult(_template);
            }
            public void Release(string key, GameObject instance)
            { if (Application.isPlaying) Destroy(instance); else DestroyImmediate(instance); }
            public void Dispose()
            { if (_template) { if (Application.isPlaying) Destroy(_template); else DestroyImmediate(_template); } }
        }
    }
}
