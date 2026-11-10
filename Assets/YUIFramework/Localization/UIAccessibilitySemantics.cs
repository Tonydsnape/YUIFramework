using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace YUIFramework.Localization
{
    public enum UIAccessibleRole { Label, Button, Toggle, Heading }
    public interface IUIAccessibilitySink
    {
        void Focused(UIAccessibilitySemantics element, string label, UIAccessibleRole role);
    }
    public sealed class UIAccessibilitySemantics : MonoBehaviour, ISelectHandler
    {
        [SerializeField] private LocalizedTextKey label;
        [SerializeField] private UIAccessibleRole role = UIAccessibleRole.Button;
        private TextLocalizationService _text;
        private IUIAccessibilitySink _sink;
        private long _generation;
        public void SetLabel(LocalizedTextKey key, UIAccessibleRole value) { label = key; role = value; }
        public IDisposable Bind(TextLocalizationService text, IUIAccessibilitySink sink)
        {
            _text = text ?? throw new ArgumentNullException(nameof(text));
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            var generation = ++_generation;
            return new BindingToken(() => { if (this && generation == _generation) { _text = null; _sink = null; } });
        }
        public void OnSelect(BaseEventData eventData)
        {
            var selectable = GetComponent<Selectable>();
            if (!isActiveAndEnabled || selectable && !selectable.IsInteractable() || _text == null) return;
            var result = _text.Resolve(label.Key);
            if (!result.HasText) throw new LocalizedTextException(result);
            if (result.Status == LocalizedTextStatus.Fallback) Debug.LogWarning(result.ToString(), this);
            _sink?.Focused(this, result.Text, role);
        }
        private void OnDestroy() { ++_generation; _text = null; _sink = null; }
    }
}
