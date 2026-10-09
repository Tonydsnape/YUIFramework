using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace YUIFramework
{
    public static class UIDataBinding
    {
        public static BindingToken BindText(
            Text text,
            IReadOnlyObservableProperty<string> property,
            BindingMode mode = BindingMode.OneWay)
        {
            return BindText(text, property, value => value ?? string.Empty, mode);
        }

        public static BindingToken BindText<T>(
            Text text,
            IReadOnlyObservableProperty<T> property,
            Func<T, string> formatter,
            BindingMode mode = BindingMode.OneWay)
        {
            EnsureNotNull(text, nameof(text));
            return BindFormattedText(
                property,
                formatter,
                value =>
                {
                    if (text != null)
                    {
                        text.text = value;
                    }
                },
                mode);
        }

        public static BindingToken BindText(
            TMP_Text text,
            IReadOnlyObservableProperty<string> property,
            BindingMode mode = BindingMode.OneWay)
        {
            return BindText(text, property, value => value ?? string.Empty, mode);
        }

        public static BindingToken BindText<T>(
            TMP_Text text,
            IReadOnlyObservableProperty<T> property,
            Func<T, string> formatter,
            BindingMode mode = BindingMode.OneWay)
        {
            EnsureNotNull(text, nameof(text));
            return BindFormattedText(
                property,
                formatter,
                value =>
                {
                    if (text != null)
                    {
                        text.text = value;
                    }
                },
                mode);
        }

        public static BindingToken BindToggle(
            Toggle toggle,
            IObservableProperty<bool> property,
            BindingMode mode = BindingMode.TwoWay)
        {
            EnsureNotNull(toggle, nameof(toggle));
            EnsureNotNull(property, nameof(property));
            var token = new BindingToken();
            if (mode == BindingMode.OneTime)
            {
                toggle.SetIsOnWithoutNotify(property.Value);
                return token;
            }

            token.Add(property.Subscribe(
                value =>
                {
                    if (toggle != null)
                    {
                        toggle.SetIsOnWithoutNotify(value);
                    }
                },
                true));
            if (mode == BindingMode.TwoWay)
            {
                UnityAction<bool> listener = value => property.Value = value;
                toggle.onValueChanged.AddListener(listener);
                token.Add(
                    () =>
                    {
                        if (toggle != null)
                        {
                            toggle.onValueChanged.RemoveListener(listener);
                        }
                    });
            }

            return token;
        }

        public static BindingToken BindSlider(
            Slider slider,
            IObservableProperty<float> property,
            BindingMode mode = BindingMode.TwoWay)
        {
            EnsureNotNull(slider, nameof(slider));
            return BindFloatControl(
                property,
                mode,
                value =>
                {
                    if (slider != null)
                    {
                        slider.SetValueWithoutNotify(value);
                    }
                },
                listener => slider.onValueChanged.AddListener(listener),
                listener =>
                {
                    if (slider != null)
                    {
                        slider.onValueChanged.RemoveListener(listener);
                    }
                });
        }

        public static BindingToken BindScrollbar(
            Scrollbar scrollbar,
            IObservableProperty<float> property,
            BindingMode mode = BindingMode.TwoWay)
        {
            EnsureNotNull(scrollbar, nameof(scrollbar));
            return BindFloatControl(
                property,
                mode,
                value =>
                {
                    if (scrollbar != null)
                    {
                        scrollbar.SetValueWithoutNotify(value);
                    }
                },
                listener => scrollbar.onValueChanged.AddListener(listener),
                listener =>
                {
                    if (scrollbar != null)
                    {
                        scrollbar.onValueChanged.RemoveListener(listener);
                    }
                });
        }

        public static BindingToken BindDropdown(
            Dropdown dropdown,
            IObservableProperty<int> property,
            BindingMode mode = BindingMode.TwoWay)
        {
            EnsureNotNull(dropdown, nameof(dropdown));
            return BindIntControl(
                property,
                mode,
                value =>
                {
                    if (dropdown != null)
                    {
                        dropdown.SetValueWithoutNotify(value);
                    }
                },
                listener => dropdown.onValueChanged.AddListener(listener),
                listener =>
                {
                    if (dropdown != null)
                    {
                        dropdown.onValueChanged.RemoveListener(listener);
                    }
                });
        }

        public static BindingToken BindDropdown(
            TMP_Dropdown dropdown,
            IObservableProperty<int> property,
            BindingMode mode = BindingMode.TwoWay)
        {
            EnsureNotNull(dropdown, nameof(dropdown));
            return BindIntControl(
                property,
                mode,
                value =>
                {
                    if (dropdown != null)
                    {
                        dropdown.SetValueWithoutNotify(value);
                    }
                },
                listener => dropdown.onValueChanged.AddListener(listener),
                listener =>
                {
                    if (dropdown != null)
                    {
                        dropdown.onValueChanged.RemoveListener(listener);
                    }
                });
        }

        public static BindingToken BindInputField(
            InputField input,
            IObservableProperty<string> property,
            BindingMode mode = BindingMode.TwoWay,
            IValidationSource validation = null,
            Text validationText = null)
        {
            return BindInputField(
                input,
                property,
                value => value ?? string.Empty,
                value => value,
                mode,
                validation,
                validationText);
        }

        public static BindingToken BindInputField<T>(
            InputField input,
            IObservableProperty<T> property,
            Func<T, string> formatter,
            Func<string, T> converter,
            BindingMode mode = BindingMode.TwoWay,
            IValidationSource validation = null,
            Text validationText = null)
        {
            EnsureNotNull(input, nameof(input));
            return BindInput(
                property,
                formatter,
                converter,
                mode,
                value =>
                {
                    if (input != null)
                    {
                        input.SetTextWithoutNotify(value);
                    }
                },
                listener => input.onValueChanged.AddListener(listener),
                listener =>
                {
                    if (input != null)
                    {
                        input.onValueChanged.RemoveListener(listener);
                    }
                },
                validation,
                error =>
                {
                    if (validationText != null)
                    {
                        validationText.text = error ?? string.Empty;
                        validationText.gameObject.SetActive(!string.IsNullOrEmpty(error));
                    }
                });
        }

        public static BindingToken BindInputField(
            TMP_InputField input,
            IObservableProperty<string> property,
            BindingMode mode = BindingMode.TwoWay,
            IValidationSource validation = null,
            TMP_Text validationText = null)
        {
            return BindInputField(
                input,
                property,
                value => value ?? string.Empty,
                value => value,
                mode,
                validation,
                validationText);
        }

        public static BindingToken BindInputField<T>(
            TMP_InputField input,
            IObservableProperty<T> property,
            Func<T, string> formatter,
            Func<string, T> converter,
            BindingMode mode = BindingMode.TwoWay,
            IValidationSource validation = null,
            TMP_Text validationText = null)
        {
            EnsureNotNull(input, nameof(input));
            return BindInput(
                property,
                formatter,
                converter,
                mode,
                value =>
                {
                    if (input != null)
                    {
                        input.SetTextWithoutNotify(value);
                    }
                },
                listener => input.onValueChanged.AddListener(listener),
                listener =>
                {
                    if (input != null)
                    {
                        input.onValueChanged.RemoveListener(listener);
                    }
                },
                validation,
                error =>
                {
                    if (validationText != null)
                    {
                        validationText.text = error ?? string.Empty;
                        validationText.gameObject.SetActive(!string.IsNullOrEmpty(error));
                    }
                });
        }

        public static BindingToken BindButton(
            Button button,
            IUICommand command)
        {
            EnsureNotNull(button, nameof(button));
            EnsureNotNull(command, nameof(command));
            var token = new BindingToken();
            var cancellation = new CancellationTokenSource();

            void UpdateState()
            {
                if (button != null)
                {
                    button.interactable = command.CanExecute;
                }
            }

            UnityAction listener = () =>
            {
                if (!command.CanExecute)
                {
                    return;
                }

                ExecuteBoundCommandAsync(command, cancellation.Token)
                    .Forget(HandleBoundCommandError);
            };
            token.Add(
                () =>
                {
                    cancellation.Cancel();
                    cancellation.Dispose();
                });
            token.Add(() => command.StateChanged -= UpdateState);
            token.Add(
                () =>
                {
                    if (button != null)
                    {
                        button.onClick.RemoveListener(listener);
                    }
                });
            try
            {
                command.StateChanged += UpdateState;
                button.onClick.AddListener(listener);
                UpdateState();
                return token;
            }
            catch (Exception setupException)
            {
                try
                {
                    token.Dispose();
                }
                catch (Exception cleanupException)
                {
                    throw new AggregateException(
                        "Button binding setup and cleanup failed.",
                        setupException,
                        cleanupException);
                }

                throw;
            }
        }

        public static BindingToken BindValidation(
            IValidationSource validation,
            Action<bool, string> update)
        {
            EnsureNotNull(validation, nameof(validation));
            EnsureNotNull(update, nameof(update));

            void Update()
            {
                update(validation.IsValid, validation.ValidationError);
            }

            validation.ValidationChanged += Update;
            Update();
            return new BindingToken(() => validation.ValidationChanged -= Update);
        }

        private static BindingToken BindFormattedText<T>(
            IReadOnlyObservableProperty<T> property,
            Func<T, string> formatter,
            Action<string> update,
            BindingMode mode)
        {
            EnsureNotNull(property, nameof(property));
            EnsureNotNull(formatter, nameof(formatter));
            if (mode == BindingMode.TwoWay)
            {
                throw new BindingException("Text bindings do not support TwoWay mode.");
            }

            void Update(T value)
            {
                update(formatter(value) ?? string.Empty);
            }

            var token = new BindingToken();
            if (mode == BindingMode.OneTime)
            {
                Update(property.Value);
            }
            else
            {
                token.Add(property.Subscribe(Update, true));
            }

            return token;
        }

        private static BindingToken BindFloatControl(
            IObservableProperty<float> property,
            BindingMode mode,
            Action<float> update,
            Action<UnityAction<float>> addListener,
            Action<UnityAction<float>> removeListener)
        {
            EnsureNotNull(property, nameof(property));
            var token = new BindingToken();
            if (mode == BindingMode.OneTime)
            {
                update(property.Value);
                return token;
            }

            token.Add(property.Subscribe(update, true));
            if (mode == BindingMode.TwoWay)
            {
                UnityAction<float> listener = value => property.Value = value;
                addListener(listener);
                token.Add(() => removeListener(listener));
            }

            return token;
        }

        private static BindingToken BindIntControl(
            IObservableProperty<int> property,
            BindingMode mode,
            Action<int> update,
            Action<UnityAction<int>> addListener,
            Action<UnityAction<int>> removeListener)
        {
            EnsureNotNull(property, nameof(property));
            var token = new BindingToken();
            if (mode == BindingMode.OneTime)
            {
                update(property.Value);
                return token;
            }

            token.Add(property.Subscribe(update, true));
            if (mode == BindingMode.TwoWay)
            {
                UnityAction<int> listener = value => property.Value = value;
                addListener(listener);
                token.Add(() => removeListener(listener));
            }

            return token;
        }

        private static BindingToken BindInput<T>(
            IObservableProperty<T> property,
            Func<T, string> formatter,
            Func<string, T> converter,
            BindingMode mode,
            Action<string> update,
            Action<UnityAction<string>> addListener,
            Action<UnityAction<string>> removeListener,
            IValidationSource validation,
            Action<string> updateValidation)
        {
            EnsureNotNull(property, nameof(property));
            EnsureNotNull(formatter, nameof(formatter));
            EnsureNotNull(converter, nameof(converter));
            var token = new BindingToken();
            if (mode == BindingMode.OneTime)
            {
                update(formatter(property.Value) ?? string.Empty);
                UpdateValidation(validation, updateValidation);
                return token;
            }

            token.Add(
                property.Subscribe(
                    value => update(formatter(value) ?? string.Empty),
                    true));
            if (mode == BindingMode.TwoWay)
            {
                UnityAction<string> listener = value => property.Value = converter(value);
                addListener(listener);
                token.Add(() => removeListener(listener));
            }

            if (validation != null)
            {
                void ValidationChanged()
                {
                    UpdateValidation(validation, updateValidation);
                }

                validation.ValidationChanged += ValidationChanged;
                token.Add(() => validation.ValidationChanged -= ValidationChanged);
                UpdateValidation(validation, updateValidation);
            }

            return token;
        }

        private static void UpdateValidation(
            IValidationSource validation,
            Action<string> update)
        {
            if (validation != null)
            {
                update(validation.ValidationError);
            }
        }

        private static async UniTask ExecuteBoundCommandAsync(
            IUICommand command,
            CancellationToken cancellationToken)
        {
            await command.ExecuteAsync(cancellationToken);
        }

        private static void HandleBoundCommandError(Exception exception)
        {
            if (!(exception is OperationCanceledException))
            {
                Debug.LogException(exception);
            }
        }

        private static void EnsureNotNull(object value, string paramName)
        {
            if (value == null ||
                value is UnityEngine.Object unityObject && unityObject == null)
            {
                throw new BindingException(
                    $"Binding failed because {paramName} is null.");
            }
        }
    }
}
