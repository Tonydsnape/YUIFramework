using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace YUIFramework
{
    /// <summary>
    /// uGUI 代码式数据绑定工具。
    /// </summary>
    public static class UIDataBinding
    {
        public static BindingToken BindText(Text text, ObservableProperty<string> property, BindingMode mode = BindingMode.OneWay)
        {
            return BindText(text, property, value => value ?? string.Empty, mode);
        }

        public static BindingToken BindText<T>(Text text, ObservableProperty<T> property, Func<T, string> formatter, BindingMode mode = BindingMode.OneWay)
        {
            EnsureNotNull(text, nameof(text));
            EnsureNotNull(property, nameof(property));
            EnsureNotNull(formatter, nameof(formatter));

            if (mode == BindingMode.TwoWay)
            {
                Debug.LogWarning("[UIDataBinding] Text 不支持输入回写，TwoWay 将按 OneWay 处理。");
                mode = BindingMode.OneWay;
            }

            void UpdateText(T value)
            {
                text.text = formatter(value) ?? string.Empty;
            }

            var token = new BindingToken();
            if (mode == BindingMode.OneTime)
            {
                UpdateText(property.Value);
                return token;
            }

            token.Add(property.Subscribe(UpdateText, true));
            return token;
        }

        public static BindingToken BindToggle(Toggle toggle, ObservableProperty<bool> property, BindingMode mode = BindingMode.TwoWay)
        {
            EnsureNotNull(toggle, nameof(toggle));
            EnsureNotNull(property, nameof(property));

            var token = new BindingToken();
            if (mode == BindingMode.OneTime)
            {
                toggle.SetIsOnWithoutNotify(property.Value);
                return token;
            }

            token.Add(property.Subscribe(value => toggle.SetIsOnWithoutNotify(value), true));

            if (mode == BindingMode.TwoWay)
            {
                UnityAction<bool> onValueChanged = value => property.Value = value;
                toggle.onValueChanged.AddListener(onValueChanged);
                token.Add(() => toggle.onValueChanged.RemoveListener(onValueChanged));
            }

            return token;
        }

        public static BindingToken BindSlider(Slider slider, ObservableProperty<float> property, BindingMode mode = BindingMode.TwoWay)
        {
            EnsureNotNull(slider, nameof(slider));
            EnsureNotNull(property, nameof(property));

            var token = new BindingToken();
            if (mode == BindingMode.OneTime)
            {
                slider.SetValueWithoutNotify(property.Value);
                return token;
            }

            token.Add(property.Subscribe(value => slider.SetValueWithoutNotify(value), true));

            if (mode == BindingMode.TwoWay)
            {
                UnityAction<float> onValueChanged = value => property.Value = value;
                slider.onValueChanged.AddListener(onValueChanged);
                token.Add(() => slider.onValueChanged.RemoveListener(onValueChanged));
            }

            return token;
        }

        private static void EnsureNotNull(object value, string paramName)
        {
            if (value == null)
            {
                throw new BindingException($"绑定失败：参数 {paramName} 不能为空。");
            }
        }
    }
}
