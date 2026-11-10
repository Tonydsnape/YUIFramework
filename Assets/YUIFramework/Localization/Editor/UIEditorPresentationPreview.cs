using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using YUIFramework.Configuration;

namespace YUIFramework.Localization.Editor
{
    // Immediate Editor-only source; never participates in production resource loading.
    public sealed class UIEditorFileConfigSource : IConfigSource
    {
        private readonly IReadOnlyDictionary<string, string> _paths;
        public UIEditorFileConfigSource(IReadOnlyDictionary<string, string> paths) { _paths = paths; }
        public UniTask<IConfigAsset> LoadAsync(string table, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return UniTask.FromResult<IConfigAsset>(new FileAsset(File.ReadAllBytes(_paths[table])));
        }
        private sealed class FileAsset : IConfigAsset
        {
            public byte[] Bytes { get; private set; }
            internal FileAsset(byte[] bytes) { Bytes = bytes; }
            public void Dispose() { Bytes = null; }
        }
    }
    [Serializable]
    public sealed class UIEditorPreviewOptions
    {
        public string Locale = "en", Theme = "light", DefaultPackage = "DefaultPackage";
        public float FontScale = 1;
        public string LocalizationJson = "Assets/YUIFramework/ConfigData/Editor/json/LocalizationTextConfig.json";
        public string PresentationJson = "Assets/YUIFramework/ConfigData/Editor/json/UIPresentationConfig.json";
    }
    // Shared by the device window and tests: inactive clone, actual catalog/service,
    // isolated TMP state, then built-in UI-only activation.
    public sealed class UIEditorPresentationPreview : IDisposable
    {
        private readonly TransientTMPFonts _fonts = new TransientTMPFonts();
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        private readonly List<IDisposable> _bindings = new List<IDisposable>();
        private readonly ConfigService _configs;
        private TextLocalizationService _localization;
        private GameObject _holder;
        public GameObject Clone { get; private set; }
        public UIEditorPresentationPreview(GameObject source, Transform parent, UIEditorPreviewOptions options,
            Func<string, string, Type, UnityEngine.Object> resolve)
        {
            if (!source) throw new ArgumentNullException(nameof(source));
            if (options.FontScale < .75f || options.FontScale > 2) throw new ArgumentOutOfRangeException(nameof(options.FontScale));
            var localizationPath = options.LocalizationJson;
            var presentationPath = options.PresentationJson;
            if (localizationPath == "Assets/YUIFramework/ConfigData/Editor/json/LocalizationTextConfig.json" && !File.Exists(localizationPath))
                localizationPath = EditorJsonConfigSource.FindSampleRoot() + "LocalizationTextConfig.json";
            if (presentationPath == "Assets/YUIFramework/ConfigData/Editor/json/UIPresentationConfig.json" && !File.Exists(presentationPath))
                presentationPath = EditorJsonConfigSource.FindSampleRoot() + "UIPresentationConfig.json";
            _configs = new ConfigService(new UIEditorFileConfigSource(new Dictionary<string, string>
            { [LocalizationTextCatalog.Table.Name] = localizationPath, [UIPresentationCatalog.Table.Name] = presentationPath }),
                new ConfigCodec(), ConfigFormat.Json, new ConfigTable[] { LocalizationTextCatalog.Table, UIPresentationCatalog.Table });
            try
            {
                var snapshot = _configs.InitializeAsync().GetAwaiter().GetResult();
                _localization = new TextLocalizationService(_configs, options.Locale);
                var catalog = snapshot.Get(UIPresentationCatalog.Table);
                _holder = new GameObject("Transient config preview");
                _holder.hideFlags = HideFlags.HideAndDontSave; _holder.SetActive(false); _holder.transform.SetParent(parent, false);
                using (_fonts.UseSettings())
                {
                    Clone = UnityEngine.Object.Instantiate(source, _holder.transform, false);
                    Clone.hideFlags = HideFlags.HideAndDontSave;
                    Clone.SetActive(true);
                    foreach (var behaviour in Clone.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if (!behaviour) throw new InvalidOperationException("Preview source contains a missing MonoBehaviour script.");
                        if (!(behaviour is UnityEngine.EventSystems.UIBehaviour) && !(behaviour is ConfigLocalizedText)) behaviour.enabled = false;
                    }
                    foreach (var text in Clone.GetComponentsInChildren<TMP_Text>(true)) _fonts.Apply(text);
                    foreach (var image in Clone.GetComponentsInChildren<Graphic>(true))
                        if (!(image is TMP_Text) && image.material) image.material = Copy(image.material);
                    foreach (var binding in Clone.GetComponentsInChildren<ConfigPresentationBinding>(true))
                        Apply(binding, catalog, options, resolve);
                    StripBusinessBehaviours(Clone, true);
                    _holder.SetActive(true);
                    foreach (var label in Clone.GetComponentsInChildren<ConfigLocalizedText>(true)) _bindings.Add(label.Bind(_localization));
                    Canvas.ForceUpdateCanvases();
                }
            }
            catch (Exception primary)
            {
                try { Dispose(); }
                catch (Exception cleanup) { throw new AggregateException("Preview construction and cleanup failed.", primary, cleanup); }
                throw;
            }
        }
        public static void StripBusinessBehaviours(GameObject clone, bool keepLocalizedText)
        {
            foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!behaviour) throw new InvalidOperationException("Preview source contains a missing MonoBehaviour script.");
                var type = behaviour.GetType();
                var visual = type.Assembly == typeof(Selectable).Assembly || type.Assembly == typeof(TMP_Text).Assembly;
                if (keepLocalizedText && behaviour is ConfigLocalizedText) continue;
                if (!visual || behaviour is UnityEngine.EventSystems.EventSystem || behaviour is UnityEngine.EventSystems.BaseInputModule)
                {
                    UnityEngine.Object.DestroyImmediate(behaviour);
                    if (behaviour) throw new InvalidOperationException("A dependent component prevents safe preview removal of " + type.FullName);
                }
            }
        }
        private void Apply(ConfigPresentationBinding binding, UIPresentationCatalog catalog, UIEditorPreviewOptions options,
            Func<string, string, Type, UnityEngine.Object> resolve)
        {
            using var data = new SerializedObject(binding);
            var color = data.FindProperty("colorToken").stringValue;
            var font = data.FindProperty("fontToken").stringValue;
            var sprite = data.FindProperty("spriteToken").stringValue;
            var material = data.FindProperty("materialToken").stringValue;
            var graphic = binding.GetComponent<Graphic>();
            if (!graphic) throw new InvalidOperationException("Presentation preview requires a Graphic: " + binding.name);
            if (!string.IsNullOrEmpty(color)) graphic.color = catalog.Get(options.Theme, options.Locale, new UIThemeToken<Color>(color)).Color(false);
            if (graphic is TMP_Text text)
            {
                if (!string.IsNullOrEmpty(font)) text.font = _fonts.Clone(Load<TMP_FontAsset>(font));
                text.fontSize *= options.FontScale; text.fontSizeMin *= options.FontScale; text.fontSizeMax *= options.FontScale;
                if (text.font) text.fontSharedMaterial = text.font.material;
            }
            else if (!string.IsNullOrEmpty(font)) throw new InvalidOperationException("Font token requires TMP_Text.");
            if (!string.IsNullOrEmpty(sprite))
            {
                if (!(graphic is Image image)) throw new InvalidOperationException("Sprite token requires Image.");
                image.sprite = Load<Sprite>(sprite);
            }
            if (!string.IsNullOrEmpty(material))
            {
                var cloned = Copy(Load<Material>(material));
                if (graphic is TMP_Text tmp) tmp.fontSharedMaterial = cloned; else graphic.material = cloned;
            }
            T Load<T>(string token) where T : UnityEngine.Object
            {
                var entry = catalog.Get(options.Theme, options.Locale, new UIThemeToken<T>(token));
                var value = resolve?.Invoke(entry.Package ?? options.DefaultPackage, entry.Value, typeof(T)) as T;
                return value ? value : throw new InvalidOperationException($"Preview asset unavailable: {entry.Package}:{entry.Value} ({typeof(T).Name})");
            }
        }
        private T Copy<T>(T original) where T : UnityEngine.Object
        {
            var copy = UnityEngine.Object.Instantiate(original); copy.hideFlags = HideFlags.HideAndDontSave; _owned.Add(copy); return copy;
        }
        public IDisposable UseSettings() => _fonts.UseSettings();
        public void Dispose()
        {
            List<Exception> failures = null;
            void Cleanup(Action action)
            {
                try { action(); }
                catch (Exception error) { (failures ??= new List<Exception>()).Add(error); }
            }
            for (var i = _bindings.Count - 1; i >= 0; i--) Cleanup(_bindings[i].Dispose);
            _bindings.Clear();
            Cleanup(() => { if (_holder) UnityEngine.Object.DestroyImmediate(_holder); });
            _holder = null; Clone = null;
            Cleanup(() => _localization?.Dispose()); _localization = null;
            Cleanup(() => _configs?.ShutdownAsync().GetAwaiter().GetResult());
            for (var i = _owned.Count - 1; i >= 0; i--)
            {
                var asset = _owned[i];
                Cleanup(() => { if (asset) UnityEngine.Object.DestroyImmediate(asset); });
            }
            _owned.Clear(); Cleanup(_fonts.Dispose);
            if (failures != null) throw new AggregateException("Preview cleanup failed after all releases were attempted.", failures);
        }
    }
}
