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
        private bool _dirty = true;
        [MenuItem("YUIFramework/Stage 11/Device and safe-area preview")]
        private static void Open() { GetWindow<UIDevicePreview>("UI device preview"); }
        private void OnGUI()
        {
            EditorGUI.BeginChangeCheck();
            _preset = EditorGUILayout.Popup("Device", _preset, Names);
            _insets = EditorGUILayout.Vector4Field("Insets L/T/R/B (pixels)", _insets);
            _prefab = (GameObject)EditorGUILayout.ObjectField("UI prefab (optional clone)", _prefab, typeof(GameObject), false);
            _dirty |= EditorGUI.EndChangeCheck();
            EditorGUILayout.HelpBox("Transient preview only. Red region is unsafe; background/blockers still cover it. No prefab/scaler settings are saved.", MessageType.Info);
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
            using (_fonts.UseSettings()) _preview.camera.Render();
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
                var clone=Instantiate(_prefab,content.transform,false); clone.hideFlags=HideFlags.HideAndDontSave;
                foreach (var text in clone.GetComponentsInChildren<TMP_Text>(true)) _fonts.Apply(text);
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
            try { _preview?.Cleanup(); }
            finally { _preview=null; _root=null; _fonts?.Dispose(); _fonts=null; }
        }
        private void OnDisable() { Cleanup(); }
    }
}
