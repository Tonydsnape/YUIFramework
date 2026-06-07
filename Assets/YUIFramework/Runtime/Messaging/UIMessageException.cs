using System;

namespace YUIFramework
{
    public sealed class UIMessageException : Exception
    {
        public string MessageName { get; }

        public UIMessageException(string messageName, string message, Exception innerException = null)
            : base(message, innerException)
        {
            MessageName = messageName;
        }
    }
}
