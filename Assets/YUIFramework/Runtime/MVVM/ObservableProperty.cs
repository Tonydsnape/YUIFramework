using System;
using System.Collections.Generic;

namespace YUIFramework
{
    /// <summary>
    /// 轻量可观察属性，提供值变化通知。
    /// </summary>
    public sealed class ObservableProperty<T>
    {
        private static readonly EqualityComparer<T> Comparer = EqualityComparer<T>.Default;
        private T _value;

        public ObservableProperty()
            : this(default)
        {
        }

        public ObservableProperty(T initialValue)
        {
            _value = initialValue;
        }

        public event Action<T, T> ValueChanged;

        public T Value
        {
            get => _value;
            set
            {
                if (Comparer.Equals(_value, value))
                {
                    return;
                }

                var oldValue = _value;
                _value = value;
                ValueChanged?.Invoke(oldValue, _value);
            }
        }

        public IDisposable Subscribe(Action<T> handler, bool notifyImmediately = true)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            void ValueChangedHandler(T _, T newValue)
            {
                handler(newValue);
            }

            ValueChanged += ValueChangedHandler;
            if (notifyImmediately)
            {
                handler(_value);
            }

            return new BindingToken(() => ValueChanged -= ValueChangedHandler);
        }

        public IDisposable Subscribe(Action<T, T> handler, bool notifyImmediately = true)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            ValueChanged += handler;
            if (notifyImmediately)
            {
                handler(_value, _value);
            }

            return new BindingToken(() => ValueChanged -= handler);
        }

        public void SetValueWithoutNotify(T value)
        {
            _value = value;
        }
    }
}
