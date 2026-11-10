using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace YUIFramework.Localization.Editor
{
    // TMP 3.0.7 exposes no setter for its cached settings. Scope the Editor-only
    // cache while rendering cloned assets, then restore it even on failure.
    internal sealed class TransientTMPFonts : IDisposable
    {
        private static readonly FieldInfo SettingsInstance = typeof(TMP_Settings).GetField(
            "s_Instance", BindingFlags.Static | BindingFlags.NonPublic);
        private readonly Dictionary<TMP_FontAsset, TMP_FontAsset> _fonts = new Dictionary<TMP_FontAsset, TMP_FontAsset>();
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        private TMP_Settings _settings;
        public TMP_FontAsset Clone(TMP_FontAsset original)
        {
            if (!original) return null;
            if (_fonts.TryGetValue(original, out var cached)) return cached;
            var font = Copy(original); _fonts.Add(original, font);
            var atlases = original.atlasTextures;
            var copies = new Texture2D[atlases.Length];
            for (var i = 0; i < copies.Length; i++) if (atlases[i]) copies[i] = Copy(atlases[i]);
            font.atlasTextures = copies;
            if (original.material)
            {
                font.material = Copy(original.material);
                if (copies.Length > 0) font.material.mainTexture = copies[0];
            }
            var fallbacks = new List<TMP_FontAsset>();
            foreach (var fallback in original.fallbackFontAssetTable) fallbacks.Add(Clone(fallback));
            font.fallbackFontAssetTable = fallbacks;
            var weights = font.fontWeightTable;
            for (var i = 0; i < weights.Length; i++)
            {
                weights[i].regularTypeface = Clone(weights[i].regularTypeface);
                weights[i].italicTypeface = Clone(weights[i].italicTypeface);
            }
            return font;
        }
        public void Apply(TMP_Text text)
        {
            var originalMaterial = text.fontSharedMaterial;
            var originalFont = text.font;
            text.font = Clone(originalFont);
            if (originalMaterial)
            {
                var material = Copy(originalMaterial);
                if (text.font && originalFont && originalMaterial.mainTexture == originalFont.atlasTexture)
                    material.mainTexture = text.font.atlasTexture;
                text.fontSharedMaterial = material;
            }
        }
        public IDisposable UseSettings()
        {
            if (SettingsInstance == null) throw new InvalidOperationException("Unsupported TMP settings cache; update Editor isolation for this TMP version.");
            var original = TMP_Settings.instance;
            if (!_settings)
            {
                _settings = Copy(original);
                var serialized = new SerializedObject(_settings);
                serialized.FindProperty("m_defaultFontAsset").objectReferenceValue = Clone(TMP_Settings.defaultFontAsset);
                var fallbacks = serialized.FindProperty("m_fallbackFontAssets");
                for (var i = 0; i < fallbacks.arraySize; i++)
                {
                    var field = fallbacks.GetArrayElementAtIndex(i);
                    field.objectReferenceValue = Clone(field.objectReferenceValue as TMP_FontAsset);
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            SettingsInstance.SetValue(null, _settings);
            return new BindingToken(() => SettingsInstance.SetValue(null, original));
        }
        private T Copy<T>(T original) where T : UnityEngine.Object
        {
            var copy = UnityEngine.Object.Instantiate(original);
            copy.name = original.name; copy.hideFlags = HideFlags.HideAndDontSave; _owned.Add(copy); return copy;
        }
        public void Dispose()
        {
            for (var i = _owned.Count - 1; i >= 0; i--) if (_owned[i]) UnityEngine.Object.DestroyImmediate(_owned[i]);
            _owned.Clear(); _fonts.Clear(); _settings = null;
        }
    }
}
