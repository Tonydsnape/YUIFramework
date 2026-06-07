using System;
using System.Collections.Generic;
using UnityEngine;

namespace YUIFramework
{
    /// <summary>
    /// ViewModel 基类，统一管理可释放对象。
    /// </summary>
    public abstract class ViewModelBase : IViewModel
    {
        private readonly List<IDisposable> _disposables = new List<IDisposable>();

        public bool IsDisposed { get; private set; }

        protected void TrackDisposable(IDisposable disposable)
        {
            if (disposable == null)
            {
                return;
            }

            if (IsDisposed)
            {
                disposable.Dispose();
                return;
            }

            _disposables.Add(disposable);
        }

        protected virtual void OnDispose()
        {
        }

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;
            OnDispose();

            for (var i = _disposables.Count - 1; i >= 0; i--)
            {
                try
                {
                    _disposables[i]?.Dispose();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }

            _disposables.Clear();
        }
    }
}
