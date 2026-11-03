using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

namespace YUIFramework.Localization.Editor
{
    public sealed class UIDevicePreview : EditorWindow
    {
        private static readonly Vector2[] Sizes = { new Vector2(1080,1920), new Vector2(1080,2340),
            new Vector2(1080,2400), new Vector2(1536,2048), new Vector2(2400,1080), new Vector2(2048,1536) };
        private static readonly string[] Names = { "16:9 portrait", "19.5:9 portrait", "20:9 portrait", "4:3 tablet", "20:9 landscape", "4:3 landscape" };
        private int _preset;
        private Vector4 _insets = new Vector4(0, 90, 0, 60);
        private GameObject _prefab;
        private PreviewRenderUtility _preview;
        private GameObject _root;
        private TransientTMPFonts _fonts;
        private UIEditorPresentationPreview _presentation;
        private UIEditorPreviewOptions _options = new UIEditorPreviewOptions();
        private Func<string, string, Type, UnityEngine.Object> _resolve;
        private bool _useConfig;
        private bool _dirty = true;
        [MenuItem("YUIFramework/Stage 11/Device and safe-area preview")]
        private static void Open() { GetWindow<UIDevicePreview>("UI device preview"); }
        public static UIDevicePreview OpenConfigured(GameObject prefab, UIEditorPreviewOptions options,
            Func<string, string, Type, UnityEngine.Object> resolve)
        {
            var window = GetWindow<UIDevicePreview>("UI device preview");
            window._prefab = prefab; window._options = options; window._resolve = resolve;
            window._useConfig = true; window._dirty = true; window.Repaint(); return window;
        }
        private void OnGUI()
        {
            EditorGUI.BeginChangeCheck();
            _preset = EditorGUILayout.Popup("Device", _preset, Names);
            _insets = EditorGUILayout.Vector4Field("Insets L/T/R/B (pixels)", _insets);
            _prefab = (GameObject)EditorGUILayout.ObjectField("UI prefab (optional clone)", _prefab, typeof(GameObject), false);
            _useConfig = EditorGUILayout.Toggle("Apply Config presentation", _useConfig);
            if (_useConfig)
            {
                _options.Locale = EditorGUILayout.TextField("Locale (en / zh-CN / fr)", _options.Locale);
                _options.Theme = EditorGUILayout.TextField("Theme", _options.Theme);
                _options.FontScale = EditorGUILayout.Slider("Font scale", _options.FontScale, .75f, 2);
                if (_resolve == null) EditorGUILayout.HelpBox("Open this preview through Stage 14 tooling with a resource profile. Missing token resources are explicit errors.", MessageType.Warning);
            }
            _dirty |= EditorGUI.EndChangeCheck();
            EditorGUILayout.HelpBox("Transient built-in uGUI/TMP preview. Business/custom scripts and EventSystems are removed from the clone. Red is unsafe; no prefab/scaler settings are saved.", MessageType.Info);
            var rect = GUILayoutUtility.GetRect(100, 10000, 100, 10000);
            if (_dirty)
            {
                Cleanup(); _dirty = false;
                try { Build(); } catch (Exception error) { Cleanup(); Debug.LogException(error); }
            }
            if (_preview == null || rect.width < 1 || rect.height < 1 || Event.current.type != EventType.Repaint) return;
            var size = Sizes[_preset]; var aspect = size.x/size.y;
            var width = Mathf.Min(rect.width, rect.height * aspect);
            rect = new Rect(rect.center.x-width/2, rect.y, width, width/aspect);
            _preview.BeginPreview(rect, GUIStyle.none);
            using (_presentation != null ? _presentation.UseSettings() : _fonts.UseSettings()) _preview.camera.Render();
            GUI.DrawTexture(rect, _preview.EndPreview(), ScaleMode.StretchToFill);
        }
        private void Build()
        {
            var size = Sizes[_preset];
            var safe = new Rect(_insets.x, _insets.w, size.x-_insets.x-_insets.z, size.y-_insets.y-_insets.w);
            var frame = new UIScreenFrame(size, safe);
            _preview = new PreviewRenderUtility();
            _fonts = new TransientTMPFonts();
            using var settings = _fonts.UseSettings();
            _preview.camera.orthographic = true;
            _preview.camera.orthographicSize = size.y/200;
            _preview.camera.transform.position = new Vector3(0,0,-20);
            _preview.camera.nearClipPlane = .01f;
            _preview.camera.farClipPlane = 100;
            _preview.camera.cullingMask = ~0;
            // uGUI excludes Preview cameras even when its Canvas mesh is built.
            // This camera still belongs exclusively to PreviewRenderUtility's scene.
            _preview.camera.cameraType = CameraType.Game;
            _preview.camera.clearFlags = CameraClearFlags.SolidColor;
            _preview.camera.backgroundColor = Color.black;
            _root = new GameObject("Transient device preview", typeof(RectTransform), typeof(Canvas), typeof(Image));
            _root.hideFlags = HideFlags.HideAndDontSave;
            _preview.AddSingleGO(_root);
            var rootRect=(RectTransform)_root.transform; rootRect.sizeDelta=size; rootRect.localScale=Vector3.one*.01f;
            var canvas=_root.GetComponent<Canvas>(); canvas.renderMode=RenderMode.WorldSpace; canvas.worldCamera=_preview.camera;
            _root.GetComponent<Image>().color=new Color(.45f,.1f,.1f);
            var content=new GameObject("Safe content",typeof(RectTransform),typeof(Image));
            content.transform.SetParent(rootRect,false); content.GetComponent<Image>().color=new Color(.1f,.23f,.2f);
            var layout=_root.AddComponent<UIScreenLayout>(); layout.enabled=false; layout.Initialize(canvas);
            layout.Apply(frame, new Rect(0,0,1,1));
            content.AddComponent<UISafeAreaContent>().Bind(layout);
            if (_prefab)
            {
                content.SetActive(false);
                if (_useConfig)
                {
                    content.SetActive(true);
                    _presentation = new UIEditorPresentationPreview(_prefab, content.transform, _options, _resolve);
                }
                else
                {
                    var clone=Instantiate(_prefab,content.transform,false); clone.hideFlags=HideFlags.HideAndDontSave;
                    foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if (!behaviour) throw new InvalidOperationException("Preview source contains a missing MonoBehaviour script.");
                        if (!(behaviour is UnityEngine.EventSystems.UIBehaviour)) behaviour.enabled = false;
                    }
                    foreach (var text in clone.GetComponentsInChildren<TMP_Text>(true)) _fonts.Apply(text);
                    UIEditorPresentationPreview.StripBusinessBehaviours(clone, false);
                }
                content.SetActive(true);
            }
            else
            {
                var text=new GameObject("Preview label",typeof(RectTransform),typeof(TextMeshProUGUI));
                text.transform.SetParent(content.transform,false);
                var textRect=(RectTransform)text.transform; textRect.anchorMin=new Vector2(.05f,.1f); textRect.anchorMax=new Vector2(.95f,.9f);
                textRect.offsetMin=textRect.offsetMax=Vector2.zero;
                var tmp=text.GetComponent<TextMeshProUGUI>(); _fonts.Apply(tmp);
                tmp.text="中文正楷\nSafe area\nBonjour"; tmp.fontSize=64; tmp.alignment=TextAlignmentOptions.Center;
            }
            Canvas.ForceUpdateCanvases();
        }
        private void Cleanup()
        {
            Exception failure = null;
            try { _presentation?.Dispose(); }
            catch (Exception error) { failure = error; }
            finally
            {
                _presentation = null;
                try { _preview?.Cleanup(); }
                catch (Exception error) { failure = failure == null ? error : new AggregateException(failure, error); }
                finally { _preview=null; _root=null; _fonts?.Dispose(); _fonts=null; }
            }
            if (failure != null) throw new AggregateException("Preview cleanup failed.", failure);
        }
        private void OnDisable() { Cleanup(); }
    }
}
