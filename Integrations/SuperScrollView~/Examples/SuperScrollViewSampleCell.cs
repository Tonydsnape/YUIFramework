using System;
using UnityEngine;
using UnityEngine.UI;

namespace YUIFramework.Integrations.Examples
{
    public sealed class SuperScrollViewSampleCell : MonoBehaviour
    {
        [SerializeField] private Text _label;
        [SerializeField] private Image _icon;
        [SerializeField] private Button _select;
        private Action _onSelect;
        public Image Icon => _icon;
        private void Awake()
        {
            if (_label == null || _icon == null || _select == null)
                throw new InvalidOperationException("Assign the cell label, icon, and selection button on the project-owned prefab.");
            _select.onClick.AddListener(Select);
        }
        public void Render(string text, bool selected, Action onSelect)
        {
            _label.text = text;
            _select.image.color = selected ? Color.cyan : Color.white;
            _onSelect = onSelect;
        }
        private void Select() => _onSelect?.Invoke();
        private void OnDisable() { _onSelect = null; }
        private void OnDestroy() { if (_select != null) _select.onClick.RemoveListener(Select); }
    }
}
