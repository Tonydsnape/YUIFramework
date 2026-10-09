using System;

namespace YUIFramework
{
    public enum UIViewModelOwnership
    {
        Owned,
        External
    }

    public interface IViewModel : IDisposable
    {
        bool IsDisposed { get; }
    }
}
