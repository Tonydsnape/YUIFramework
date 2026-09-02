using System;

namespace YUIFramework
{
    public interface IViewModel : IDisposable
    {
        bool IsDisposed { get; }
    }
}
