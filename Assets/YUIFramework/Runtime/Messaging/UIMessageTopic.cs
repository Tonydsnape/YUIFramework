using System;

namespace YUIFramework
{
    public readonly struct UIMessageTopic<T> : IEquatable<UIMessageTopic<T>>
    {
        public UIMessageTopic(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A message topic name is required.", nameof(name));
            }

            Name = name.Trim();
        }

        public string Name { get; }

        public bool Equals(UIMessageTopic<T> other)
        {
            return string.Equals(Name, other.Name, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is UIMessageTopic<T> other && Equals(other);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Name ?? string.Empty);
        }

        public override string ToString()
        {
            return Name ?? string.Empty;
        }

        public static bool operator ==(UIMessageTopic<T> left, UIMessageTopic<T> right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(UIMessageTopic<T> left, UIMessageTopic<T> right)
        {
            return !left.Equals(right);
        }
    }

    public readonly struct UIMessageUnit
    {
        public static readonly UIMessageUnit Value = new UIMessageUnit();
    }
}
