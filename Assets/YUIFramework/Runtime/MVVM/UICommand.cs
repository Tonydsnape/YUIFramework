using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace YUIFramework
{
    public interface IUICommand : IDisposable
    {
        bool CanExecute { get; }
        bool IsExecuting { get; }
        bool IsDisposed { get; }
        Exception LastError { get; }
        event Action StateChanged;
        void NotifyCanExecuteChanged();
        UniTask ExecuteAsync(CancellationToken cancellationToken = default);
    }

    public sealed class UICommand : IUICommand
    {
        private readonly UIAsyncCommand _inner;

        public UICommand(Action execute, Func<bool> canExecute = null)
        {
            if (execute == null)
            {
                throw new ArgumentNullException(nameof(execute));
            }

            _inner = new UIAsyncCommand(
                _ =>
                {
                    execute();
                    return UniTask.CompletedTask;
                },
                canExecute);
        }

        public bool CanExecute => _inner.CanExecute;
        public bool IsExecuting => _inner.IsExecuting;
        public bool IsDisposed => _inner.IsDisposed;
        public Exception LastError => _inner.LastError;

        public event Action StateChanged
        {
            add => _inner.StateChanged += value;
            remove => _inner.StateChanged -= value;
        }

        public void NotifyCanExecuteChanged()
        {
            _inner.NotifyCanExecuteChanged();
        }

        public UniTask ExecuteAsync(CancellationToken cancellationToken = default)
        {
            return _inner.ExecuteAsync(cancellationToken);
        }

        public void Dispose()
        {
            _inner.Dispose();
        }
    }

    public sealed class UIAsyncCommand : IUICommand
    {
        private readonly Func<CancellationToken, UniTask> _execute;
        private readonly Func<bool> _canExecute;
        private readonly CancellationTokenSource _lifetimeCancellation =
            new CancellationTokenSource();
        private CancellationTokenSource _executionCancellation;

        public UIAsyncCommand(
            Func<CancellationToken, UniTask> execute,
            Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute =>
            !IsDisposed &&
            !IsExecuting &&
            (_canExecute == null || _canExecute());

        public bool IsExecuting { get; private set; }
        public bool IsDisposed { get; private set; }
        public Exception LastError { get; private set; }
        public event Action StateChanged;

        public void NotifyCanExecuteChanged()
        {
            ThrowIfDisposed();
            var errors = NotifyStateChanged();
            if (errors != null)
            {
                throw errors;
            }
        }

        public async UniTask ExecuteAsync(
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (IsExecuting)
            {
                throw new InvalidOperationException(
                    "The command is already executing.");
            }

            if (!CanExecute)
            {
                throw new InvalidOperationException(
                    "The command cannot execute in its current state.");
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetimeCancellation.Token);
            _executionCancellation = linked;
            IsExecuting = true;
            LastError = null;
            List<Exception> observerErrors = null;
            AddErrors(ref observerErrors, NotifyStateChanged());
            Exception executionError = null;
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                await _execute(linked.Token);
            }
            catch (Exception exception)
            {
                LastError = exception;
                executionError = exception;
            }
            finally
            {
                if (ReferenceEquals(_executionCancellation, linked))
                {
                    _executionCancellation = null;
                }

                IsExecuting = false;
                AddErrors(ref observerErrors, NotifyStateChanged());
            }

            if (observerErrors != null)
            {
                if (executionError != null)
                {
                    observerErrors.Insert(0, executionError);
                }

                throw new AggregateException(
                    "Command execution or state notification failed.",
                    observerErrors);
            }

            if (executionError != null)
            {
                ExceptionDispatchInfo.Capture(executionError).Throw();
            }
        }

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;
            List<Exception> errors = null;
            try
            {
                if (!_lifetimeCancellation.IsCancellationRequested)
                {
                    _lifetimeCancellation.Cancel();
                }
            }
            catch (Exception exception)
            {
                errors = new List<Exception> { exception };
            }
            finally
            {
                _executionCancellation = null;
                _lifetimeCancellation.Dispose();
            }

            AddErrors(ref errors, NotifyStateChanged());
            StateChanged = null;
            if (errors != null)
            {
                throw new AggregateException("Command disposal failed.", errors);
            }
        }

        private AggregateException NotifyStateChanged()
        {
            var handlers = StateChanged;
            if (handlers == null)
            {
                return null;
            }

            List<Exception> errors = null;
            var invocationList = handlers.GetInvocationList();
            for (var index = 0; index < invocationList.Length; index++)
            {
                try
                {
                    ((Action)invocationList[index])();
                }
                catch (Exception exception)
                {
                    errors ??= new List<Exception>();
                    errors.Add(exception);
                }
            }

            return errors == null
                ? null
                : new AggregateException(
                    "One or more command state observers failed.",
                    errors);
        }

        private static void AddErrors(
            ref List<Exception> target,
            AggregateException error)
        {
            if (error == null)
            {
                return;
            }

            target ??= new List<Exception>();
            target.AddRange(error.InnerExceptions);
        }

        private void ThrowIfDisposed()
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(nameof(UIAsyncCommand));
            }
        }
    }
}
