using System;

namespace YUIFramework
{
    public interface IValidationSource
    {
        bool IsValid { get; }
        string ValidationError { get; }
        event Action ValidationChanged;
    }

    public sealed class ValidatedProperty<T> :
        IObservableProperty<T>,
        IValidationSource
    {
        private readonly ObservableProperty<T> _property;
        private readonly Func<T, string> _validator;

        public ValidatedProperty(
            T initialValue,
            Func<T, string> validator)
        {
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _property = new ObservableProperty<T>(initialValue);
            Validate(initialValue);
        }

        public T Value
        {
            get => _property.Value;
            set
            {
                var previousError = ValidationError;
                var nextError = _validator(value);
                ValidationError = nextError;
                _property.Value = value;
                if (!string.Equals(
                        previousError,
                        nextError,
                        StringComparison.Ordinal))
                {
                    ValidationChanged?.Invoke();
                }
            }
        }

        public bool IsValid => string.IsNullOrEmpty(ValidationError);
        public string ValidationError { get; private set; }
        public event Action ValidationChanged;

        public IDisposable Subscribe(
            Action<T> handler,
            bool notifyImmediately = true)
        {
            return _property.Subscribe(handler, notifyImmediately);
        }

        public IDisposable Subscribe(
            Action<T, T> handler,
            bool notifyImmediately = true)
        {
            return _property.Subscribe(handler, notifyImmediately);
        }

        public void SetValueWithoutNotify(T value)
        {
            var previousError = ValidationError;
            var nextError = _validator(value);
            ValidationError = nextError;
            _property.SetValueWithoutNotify(value);
            if (!string.Equals(
                    previousError,
                    nextError,
                    StringComparison.Ordinal))
            {
                ValidationChanged?.Invoke();
            }
        }

        public void Revalidate()
        {
            var previousError = ValidationError;
            var nextError = _validator(Value);
            ValidationError = nextError;
            if (!string.Equals(
                    previousError,
                    nextError,
                    StringComparison.Ordinal))
            {
                ValidationChanged?.Invoke();
            }
        }

        private void Validate(T value)
        {
            ValidationError = _validator(value);
        }
    }
}
