using System;
using System.Threading;

namespace YUIFramework
{
    [Serializable]
    public readonly struct UIOperationId : IEquatable<UIOperationId>
    {
        private static long _sequence;

        internal static UIOperationId Next()
        {
            return new UIOperationId(Interlocked.Increment(ref _sequence));
        }

        public UIOperationId(long value)
        {
            Value = value;
        }

        public long Value { get; }
        public bool IsValid => Value > 0;

        public bool Equals(UIOperationId other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is UIOperationId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public override string ToString()
        {
            return IsValid ? Value.ToString() : "None";
        }

        public static bool operator ==(UIOperationId left, UIOperationId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(UIOperationId left, UIOperationId right)
        {
            return !left.Equals(right);
        }
    }
}
