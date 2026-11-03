using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace YUIFramework
{
    public enum UIDialogResult { Confirmed, Declined, Dismissed }

    public sealed class UIConfirmationRequest
    {
        public UIConfirmationRequest(string title, string message,
            string confirmLabel = "OK", string cancelLabel = "Cancel", bool allowCancel = true)
        {
            if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("A title is required.", nameof(title));
            if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("A message is required.", nameof(message));
            if (string.IsNullOrWhiteSpace(confirmLabel) || (allowCancel && string.IsNullOrWhiteSpace(cancelLabel)))
                throw new ArgumentException("Action labels are required.", nameof(confirmLabel));
            Title = title; Message = message; ConfirmLabel = confirmLabel;
            CancelLabel = cancelLabel; AllowCancel = allowCancel;
        }
        public string Title { get; }
        public string Message { get; }
        public string ConfirmLabel { get; }
        public string CancelLabel { get; }
        public bool AllowCancel { get; }
    }

    public sealed class UIConfirmationSession
    {
        private readonly UniTaskCompletionSource<UIDialogResult> _completion =
            new UniTaskCompletionSource<UIDialogResult>();
        internal UIConfirmationSession(UIConfirmationRequest request) { Request = request; }
        public UIConfirmationRequest Request { get; }
        public bool IsCompleted { get; private set; }
        internal UniTask<UIDialogResult> Completion => _completion.Task;
        public bool Confirm() => Complete(UIDialogResult.Confirmed);
        public bool Decline() => Request.AllowCancel && Complete(UIDialogResult.Declined);
        internal bool Complete(UIDialogResult result)
        {
            if (IsCompleted) return false;
            IsCompleted = true;
            return _completion.TrySetResult(result);
        }
    }

    public abstract class UIConfirmationContext : BaseDialogContext, IUIBackHandler
    {
        public UIConfirmationSession Session { get; private set; }
        public bool TryHandleBack() => Session?.Decline() ?? false;
        protected sealed override void HandleShow(object args)
        {
            if (!(args is UIConfirmationSession session))
                throw new ArgumentException("A confirmation session is required.", nameof(args));
            if (Session != null && !Session.IsCompleted)
                throw new InvalidOperationException("The previous dialog session is still active.");
            Session = session;
            Present(session);
        }
        protected abstract void Present(UIConfirmationSession session);
        protected sealed override void HandleHide()
        {
            Session?.Complete(UIDialogResult.Dismissed);
            Session = null;
        }
        protected override void HandleDestroy()
        {
            Session?.Complete(UIDialogResult.Dismissed);
            Session = null;
        }
    }

    public sealed class UIConfirmationService<TContext> where TContext : UIConfirmationContext
    {
        private readonly IUIService _ui;
        private readonly UIOperationCoordinator _queue = new UIOperationCoordinator();
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private bool _stopped;
        private Task _shutdown;
        public UIConfirmationService(IUIService ui) { _ui = ui ?? throw new ArgumentNullException(nameof(ui)); }

        public UniTask<UIDialogResult> ConfirmAsync(UIScope owner, UIConfirmationRequest request,
            CancellationToken cancellationToken = default)
        {
            if (_stopped) throw new ObjectDisposedException(nameof(UIConfirmationService<TContext>));
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (request == null) throw new ArgumentNullException(nameof(request));
            return owner.RunAsync(async scopeToken =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(scopeToken, _lifetime.Token);
                return await _queue.EnqueueAsync(typeof(TContext), "Confirm",
                    ct => PresentAsync(owner, request, ct), linked.Token);
            }, cancellationToken);
        }

        private async UniTask<UIDialogResult> PresentAsync(UIScope owner,
            UIConfirmationRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var session = new UIConfirmationSession(request);
            TContext context = null;
            Exception failure = null;
            try
            {
                context = await owner.OpenAsync<TContext>(session, token);
                return await session.Completion.AttachExternalCancellation(token);
            }
            catch (Exception exception) { failure = exception; throw; }
            finally
            {
                session.Complete(UIDialogResult.Dismissed);
                // Hide/Destroy may complete the session inside a guarded lifecycle callback.
                await UniTask.Yield(PlayerLoopTiming.Update);
                // Cancellation can win the caller's wait after the manager has published the view.
                var published = _ui.Get<TContext>();
                if (context == null && ReferenceEquals(published?.Session, session)) context = published;
                if (context != null && ReferenceEquals(context.Session, session) &&
                    !owner.IsEnded && _ui.IsInitialized)
                {
                    try { await _ui.CloseAsync(context); }
                    catch (Exception cleanup)
                    {
                        if (failure != null) throw new AggregateException(failure, cleanup);
                        throw;
                    }
                }
            }
        }

        public UniTask ShutdownAsync()
        {
            if (_shutdown != null) return _shutdown.AsUniTask();
            var completion = new TaskCompletionSource<object>();
            _shutdown = completion.Task;
            _stopped = true;
            CompleteStopAsync(completion).Forget(Debug.LogException);
            return _shutdown.AsUniTask();
        }

        private async UniTask CompleteStopAsync(TaskCompletionSource<object> completion)
        {
            try { await StopCoreAsync(); completion.TrySetResult(null); }
            catch (Exception exception) { completion.TrySetException(exception); }
        }

        private async UniTask StopCoreAsync()
        {
            _queue.Stop();
            try { _lifetime.Cancel(); }
            finally
            {
                await _queue.DrainAsync();
                _lifetime.Dispose();
            }
        }
    }

    public enum UISystemNotice { Disconnected, Maintenance, ForceUpdate }
    public enum UISystemDecision { Retry, Acknowledged, UpdateRequired, Dismissed }

    public sealed class UISystemUIService<TContext> where TContext : UIConfirmationContext
    {
        private readonly UIConfirmationService<TContext> _dialogs;
        private readonly UIScope _global;
        public UISystemUIService(IUIService ui)
        {
            if (ui == null) throw new ArgumentNullException(nameof(ui));
            _global = ui.Scopes.Global;
            _dialogs = new UIConfirmationService<TContext>(ui);
        }

        public async UniTask<UISystemDecision> ShowAsync(UISystemNotice notice,
            UIConfirmationRequest request, CancellationToken cancellationToken = default)
        {
            if (!Enum.IsDefined(typeof(UISystemNotice), notice)) throw new ArgumentOutOfRangeException(nameof(notice));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (notice != UISystemNotice.Disconnected && request.AllowCancel)
                throw new ArgumentException("Maintenance/force-update notices require a non-declinable action.", nameof(request));
            var result = await _dialogs.ConfirmAsync(_global, request, cancellationToken);
            if (result != UIDialogResult.Confirmed) return UISystemDecision.Dismissed;
            return notice == UISystemNotice.Disconnected ? UISystemDecision.Retry :
                notice == UISystemNotice.Maintenance ? UISystemDecision.Acknowledged : UISystemDecision.UpdateRequired;
        }
        public UniTask ShutdownAsync() => _dialogs.ShutdownAsync();
    }
}
