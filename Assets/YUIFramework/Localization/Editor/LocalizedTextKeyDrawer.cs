using System;
using System.IO;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using Newtonsoft.Json;
using YUIFramework.Configuration;

namespace YUIFramework.Localization.Editor
{
    public static class LocalizedTextEditorCatalog
    {
        public const string RelativePath = "Assets/YUIFramework/ConfigData/Editor/json/LocalizationTextConfig.json";
        public static LocalizationTextCatalog Load(string path) =>
            LocalizationTextCatalog.Parse(new ConfigCodec().Decode(File.ReadAllBytes(path), ConfigFormat.Json));
        public static void ApplySelection(UnityEngine.Object[] targets, string propertyPath, string key)
        {
            Load(RelativePath).Get(key);
            var serialized = new SerializedObject(targets);
            serialized.Update();
            var selected = serialized.FindProperty(propertyPath) ??
                throw new InvalidOperationException("The localization key property no longer exists.");
            selected.stringValue = key;
            serialized.ApplyModifiedProperties();
        }
    }

    [CustomPropertyDrawer(typeof(LocalizedTextKey))]
    public sealed class LocalizedTextKeyDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) =>
            EditorGUIUtility.singleLineHeight * 2 + EditorGUIUtility.standardVerticalSpacing;
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            try
            {
                var field = property.FindPropertyRelative("key");
                var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
                var preview = new Rect(position.x, line.yMax + EditorGUIUtility.standardVerticalSpacing,
                    position.width, EditorGUIUtility.singleLineHeight);
                LocalizationTextCatalog catalog;
                try { catalog = LocalizedTextEditorCatalog.Load(LocalizedTextEditorCatalog.RelativePath); }
                catch (Exception error) when (error is IOException || error is ConfigDataException || error is JsonException)
                {
                    EditorGUI.LabelField(line, label, new GUIContent(field.stringValue));
                    EditorGUI.HelpBox(preview, "Export LocalizationTextConfig first: " + error.Message, MessageType.Error);
                    return;
                }
                var valid = catalog.TryGet(field.stringValue, out var entry);
                var description = field.hasMultipleDifferentValues ? "(mixed)" : field.stringValue;
                if (GUI.Button(EditorGUI.PrefixLabel(line, label),
                    string.IsNullOrEmpty(description) ? "Select localization key..." : description, EditorStyles.popup))
                {
                    var targets = property.serializedObject.targetObjects;
                    var path = field.propertyPath;
                    new KeyDropdown(catalog, key => LocalizedTextEditorCatalog.ApplySelection(targets, path, key)).Show(line);
                }
                if (valid) EditorGUI.LabelField(preview, entry.Translations["zh-CN"] ?? entry.Translations["en"] ?? "(blank)");
                else EditorGUI.HelpBox(preview, "Choose a valid exported key. Unknown keys fail explicitly at runtime.", MessageType.Warning);
            }
            finally { EditorGUI.EndProperty(); }
        }
        private sealed class KeyDropdown : AdvancedDropdown
        {
            private readonly LocalizationTextCatalog _catalog;
            private readonly Action<string> _select;
            internal KeyDropdown(LocalizationTextCatalog catalog, Action<string> select) : base(new AdvancedDropdownState())
            { _catalog = catalog; _select = select; minimumSize = new Vector2(340, 260); }
            protected override AdvancedDropdownItem BuildRoot()
            {
                var root = new AdvancedDropdownItem("Localization text");
                for (var i = 0; i < _catalog.Keys.Count; i++)
                    root.AddChild(new AdvancedDropdownItem(_catalog.Keys[i]) { id = i });
                return root;
            }
            protected override void ItemSelected(AdvancedDropdownItem item) { _select(_catalog.Keys[item.id]); }
        }
    }
}
