using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace YUIFramework.Localization.Editor
{
    public static class PresentationSampleAssets
    {
        public const string Folder = "Assets/Resources/YUIPresentation";
        [MenuItem("YUIFramework/Stage 11/Create missing sample assets")]
        public static void Create()
        {
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
            var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/YUIFramework/ThirdParty/LXGWWenKai/LXGWWenKai-Regular.ttf");
            if (!source) throw new InvalidOperationException("Install the verified WenKai Regular font first.");
            var primaryPath = Folder + "/WenKai.asset";
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(primaryPath);
            if (!font)
            {
                font = CreateFont(source, "WenKai", 2048);
                if (!font.TryAddCharacters(SampleCharacters(), out var missing))
                    throw new InvalidOperationException("Base font lacks sample glyphs: " + missing);
                font.atlasPopulationMode = AtlasPopulationMode.Static;
                var fallback = CreateFont(source, "WenKaiFallback", 2048);
                font.fallbackFontAssetTable = new List<TMP_FontAsset> { fallback };
                SaveFont(fallback, Folder + "/WenKaiFallback.asset");
                SaveFont(font, primaryPath);
            }
            const string settingsPath = "Assets/Resources/TMP Settings.asset";
            if (!AssetDatabase.LoadAssetAtPath<TMP_Settings>(settingsPath))
            {
                var settings = ScriptableObject.CreateInstance<TMP_Settings>();
                var serialized = new SerializedObject(settings);
                serialized.FindProperty("m_defaultFontAsset").objectReferenceValue = font;
                serialized.FindProperty("m_defaultFontSize").floatValue = 32;
                serialized.FindProperty("m_defaultAutoSizeMinRatio").floatValue = .5f;
                serialized.FindProperty("m_defaultAutoSizeMaxRatio").floatValue = 2;
                serialized.FindProperty("m_warningsDisabled").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.CreateAsset(settings, settingsPath);
            }
            var settingsAsset = AssetDatabase.LoadAssetAtPath<TMP_Settings>(settingsPath);
            var settingsProperties = new SerializedObject(settingsAsset);
            foreach (var pair in new[] { ("m_leadingCharacters", "LineBreaking Leading Characters.txt"),
                ("m_followingCharacters", "LineBreaking Following Characters.txt") })
            {
                var property = settingsProperties.FindProperty(pair.Item1);
                if (!property.objectReferenceValue)
                {
                    var rules = AssetDatabase.LoadAssetAtPath<TextAsset>(Folder + "/" + pair.Item2);
                    if (!rules) throw new InvalidOperationException("Install the licensed TMP line-breaking resources.");
                    property.objectReferenceValue = rules;
                }
            }
            settingsProperties.ApplyModifiedPropertiesWithoutUndo();
            CreateBadge("BadgeEn", new Color32(50, 120, 210, 255));
            CreateBadge("BadgeZh", new Color32(180, 60, 45, 255));
            CreateBadge("BadgeFr", new Color32(85, 65, 170, 255));
            var materialPath = Folder + "/Panel.mat";
            if (!AssetDatabase.LoadAssetAtPath<Material>(materialPath))
                AssetDatabase.CreateAsset(new Material(Shader.Find("UI/Default")) { name = "Panel" }, materialPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Stage11 sample assets created; existing assets and GUIDs preserved.");
        }
        private static string SampleCharacters()
        {
            var catalog = LocalizedTextEditorCatalog.Load(LocalizedTextEditorCatalog.RelativePath);
            var chars = new HashSet<char>(Enumerable.Range(32, 95).Select(value => (char)value));
            foreach (var key in catalog.Keys)
                foreach (var text in catalog.Get(key).Translations.Values)
                    if (text != null) foreach (var c in text) chars.Add(c);
            foreach (var c in "浅色深色高对比度字号减少动画焦点语言主题返回中文正楷测试安全区域￥€éàçœ\u00A0\u2026")
                chars.Add(c);
            return new string(chars.OrderBy(c => c).ToArray());
        }
        [MenuItem("YUIFramework/Stage 11/Warm sample font from exported text")]
        public static void WarmSampleFont()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Folder + "/WenKai.asset");
            if (!font) throw new InvalidOperationException("Create sample font assets first.");
            var mode = font.atlasPopulationMode;
            try
            {
                font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                if (!font.TryAddCharacters(SampleCharacters(), out var missing))
                    throw new InvalidOperationException("Base atlas full or unsupported glyphs: " + missing);
            }
            finally { font.atlasPopulationMode = mode; }
            foreach (var asset in new[] { font, font.fallbackFontAssetTable[0] })
            {
                asset.ReadFontAssetDefinition();
                ShaderUtilities.UpdateShaderRatios(asset.material);
                EditorUtility.SetDirty(asset); EditorUtility.SetDirty(asset.material);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Sample glyph warm-up preserved existing font/atlas GUIDs.");
        }
        private static TMP_FontAsset CreateFont(Font source, string name, int size)
        {
            var font = TMP_FontAsset.CreateFontAsset(source, 48, 5, GlyphRenderMode.SDFAA, size, size,
                AtlasPopulationMode.Dynamic, false);
            if (!font || !font.material || !font.material.shader) throw new InvalidOperationException("TMP Distance Field shader missing.");
            font.name = name;
            font.material.name = name + " Material";
            font.atlasTextures[0].name = name + " Atlas";
            return font;
        }
        private static void SaveFont(TMP_FontAsset font, string path)
        {
            font.ReadFontAssetDefinition(); ShaderUtilities.UpdateShaderRatios(font.material);
            AssetDatabase.CreateAsset(font, path);
            AssetDatabase.AddObjectToAsset(font.material, font);
            foreach (var atlas in font.atlasTextures) AssetDatabase.AddObjectToAsset(atlas, font);
            EditorUtility.SetDirty(font);
        }
        private static void CreateBadge(string name, Color32 color)
        {
            var path = Folder + "/" + name + ".png";
            if (File.Exists(path)) return;
            var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            var pixels = new Color32[1024];
            for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++)
                pixels[y * 32 + x] = x >= 12 && x < 20 || y >= 12 && y < 20 ? new Color32(255, 255, 255, 255) : color;
            texture.SetPixels32(pixels); texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
    }
}
