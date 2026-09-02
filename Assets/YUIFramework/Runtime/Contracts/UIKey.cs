using System;

namespace YUIFramework
{
    [Serializable]
    public readonly struct UIKey : IEquatable<UIKey>
    {
        public UIKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("UI key cannot be empty.", nameof(value));
            }

            Value = value.Trim();
        }

        public string Value { get; }
        public bool IsValid => !string.IsNullOrWhiteSpace(Value);

        public bool Equals(UIKey other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is UIKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        }

        public override string ToString()
        {
            return Value ?? string.Empty;
        }

        public static bool operator ==(UIKey left, UIKey right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(UIKey left, UIKey right)
        {
            return !left.Equals(right);
        }
    }
}
