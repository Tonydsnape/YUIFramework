using System;

namespace YUIFramework
{
    public sealed class UIRootUnavailableException : InvalidOperationException
    {
        public UIRootUnavailableException(string message)
            : base(message)
        {
        }
    }
}
