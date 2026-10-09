using System;

namespace YUIFramework
{
    public interface IReadOnlyObservableProperty<T>
    {
        T Value { get; }
        IDisposable Subscribe(Action<T> handler, bool notifyImmediately = true);
        IDisposable Subscribe(Action<T, T> handler, bool notifyImmediately = true);
    }

    public interface IObservableProperty<T> : IReadOnlyObservableProperty<T>
    {
        new T Value { get; set; }
        void SetValueWithoutNotify(T value);
    }

    public sealed class ReadOnlyObservableProperty<T> : IReadOnlyObservableProperty<T>
    {
        private readonly IReadOnlyObservableProperty<T> _source;

        public ReadOnlyObservableProperty(IReadOnlyObservableProperty<T> source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public T Value => _source.Value;

        public IDisposable Subscribe(Action<T> handler, bool notifyImmediately = true)
        {
            return _source.Subscribe(handler, notifyImmediately);
        }

        public IDisposable Subscribe(
            Action<T, T> handler,
            bool notifyImmediately = true)
        {
            return _source.Subscribe(handler, notifyImmediately);
        }
    }
}
