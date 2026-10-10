using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace YUIFramework.Localization.Editor
{
    public readonly struct UIContentIssue
    {
        public UIContentIssue(UnityEngine.Object target, string code, string message)
        { Target = target; Code = code; Message = message; }
        public UnityEngine.Object Target { get; }
        public string Code { get; }
        public string Message { get; }
    }
    public static class UIContentChecks
    {
        public static IReadOnlyList<UIContentIssue> Inspect(GameObject root, LocalizationTextCatalog catalog)
        {
            if (!root) throw new ArgumentNullException(nameof(root));
            var issues = new List<UIContentIssue>();
            foreach (var label in root.GetComponentsInChildren<ConfigLocalizedText>(true))
                if (catalog == null || !catalog.TryGet(label.TextKey.Key, out _))
                    issues.Add(new UIContentIssue(label, "MissingKey", $"Unknown localization key '{label.TextKey.Key}'."));
            using var fonts = new TransientTMPFonts();
            using var settings = fonts.UseSettings();
            var holder = new GameObject("Transient content check", typeof(RectTransform), typeof(Canvas));
            holder.hideFlags = HideFlags.HideAndDontSave; holder.SetActive(false);
            holder.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            try
            {
                var clone = UnityEngine.Object.Instantiate(root, holder.transform, false);
                clone.hideFlags = HideFlags.HideAndDontSave;
                if (clone.transform is RectTransform cloneRect && root.transform is RectTransform sourceRect)
                { cloneRect.anchorMin = cloneRect.anchorMax = Vector2.zero; cloneRect.sizeDelta = sourceRect.rect.size; }
                foreach (var canvas in clone.GetComponentsInChildren<Canvas>(true)) canvas.renderMode = RenderMode.WorldSpace;
                foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
                    if (!(behaviour is UnityEngine.EventSystems.UIBehaviour)) behaviour.enabled = false;
                var originals = root.GetComponentsInChildren<TMP_Text>(true);
                var copies = clone.GetComponentsInChildren<TMP_Text>(true);
                foreach (var text in copies) fonts.Apply(text);
                holder.SetActive(true);
                if (clone.transform is RectTransform rect) LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
                for (var i = 0; i < copies.Length; i++) InspectText(copies[i], originals[i], issues);
            }
            finally { UnityEngine.Object.DestroyImmediate(holder); }
            return issues;
        }
        private static void InspectText(TMP_Text text, TMP_Text original, List<UIContentIssue> issues)
        {
                if (!text.font) { issues.Add(new UIContentIssue(original, "MissingFont", "No TMP font asset assigned.")); return; }
                text.ForceMeshUpdate(true, true);
                for (var i = 0; i < text.textInfo.characterCount; i++)
                {
                    var character = text.textInfo.characterInfo[i];
                    if (character.elementType == TMP_TextElementType.Sprite || char.IsWhiteSpace(character.character)) continue;
                    var index = character.index;
                    var scalar = index >= 0 && index < text.text.Length ? char.ConvertToUtf32(text.text, index) : character.character;
                    var encoded = char.ConvertFromUtf32(scalar);
                    if (!text.font.HasCharacters(encoded, out uint[] _, true, false))
                        issues.Add(new UIContentIssue(original, "MissingGlyph", $"Missing glyph U+{scalar:X4} in {text.font.name}."));
                }
                if (text.isTextOverflowing)
                    issues.Add(new UIContentIssue(original, "Overflow", $"TMP layout overflows {text.rectTransform.rect.size}; first character {text.firstOverflowCharacterIndex}."));
        }
        [MenuItem("YUIFramework/Stage 11/Check selected text content")]
        public static void CheckSelection()
        {
            if (!Selection.activeGameObject) throw new InvalidOperationException("Select a UI hierarchy to check.");
            var catalog = LocalizedTextEditorCatalog.Load(LocalizedTextEditorCatalog.RelativePath);
            var issues = Inspect(Selection.activeGameObject, catalog);
            foreach (var issue in issues) Debug.LogError($"[{issue.Code}] {issue.Target.name}: {issue.Message}", issue.Target);
            if (issues.Count == 0) Debug.Log("Selected UI content has no missing keys/glyphs/overflow.", Selection.activeGameObject);
        }
    }
}
