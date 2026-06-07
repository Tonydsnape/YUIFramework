using System;
using System.Collections.Generic;
using UnityEngine;

namespace YUIFramework
{
    /// <summary>
    /// 绑定生命周期 token，可组合多个解除操作。
    /// </summary>
    public sealed class BindingToken : IDisposable
    {
        private readonly List<Action> _disposeActions = new List<Action>();

        public BindingToken()
        {
        }

        public BindingToken(Action disposeAction)
        {
            Add(disposeAction);
        }

        public bool IsDisposed { get; private set; }

        public void Add(IDisposable disposable)
        {
            if (disposable == null)
            {
                return;
            }

            Add(disposable.Dispose);
        }

        public void Add(Action disposeAction)
        {
            if (disposeAction == null)
            {
                return;
            }

            if (IsDisposed)
            {
                disposeAction();
                return;
            }

            _disposeActions.Add(disposeAction);
        }

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;
            for (var i = _disposeActions.Count - 1; i >= 0; i--)
            {
                try
                {
                    _disposeActions[i]?.Invoke();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }

            _disposeActions.Clear();
        }
    }
}
