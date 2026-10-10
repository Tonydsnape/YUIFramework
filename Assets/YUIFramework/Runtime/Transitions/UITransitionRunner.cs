using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace YUIFramework
{
    /// <summary>
    /// Owns one generation-fenced visual writer per view.
    /// </summary>
    public sealed class UITransitionRunner : IDisposable
    {
        private readonly Dictionary<int, TargetState> _targets =
            new Dictionary<int, TargetState>();
        private readonly Dictionary<string, IUITransition> _customTransitions =
            new Dictionary<string, IUITransition>(StringComparer.Ordinal);
        private readonly IUITransitionClock _clock;
        private bool _disposed;
        public bool ReducedMotion { get; private set; }
        public bool IsDisposed => _disposed;
        public void SetReducedMotion(bool enabled)
        {
            ThrowIfDisposed();
            ReducedMotion = enabled;
            if (!enabled) return;
            var targets = new List<TargetState>(_targets.Values);
            foreach (var state in targets)
                if (state.Active != null) RequestInterruption(state.Target, UITransitionInterruption.SkipToEnd);
        }

        public UITransitionRunner(IUITransitionClock clock = null)
        {
            _clock = clock ?? new UnityUITransitionClock();
        }

        public void RegisterCustom(string id, IUITransition transition)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A custom transition id is required.", nameof(id));
            }

            _customTransitions[id] = transition ??
                throw new ArgumentNullException(nameof(transition));
        }

        public bool UnregisterCustom(string id)
        {
            ThrowIfDisposed();
            return !string.IsNullOrWhiteSpace(id) && _customTransitions.Remove(id);
        }

        public UniTask PlayShowAsync(
            RectTransform target,
            UITransitionOptions options,
            CancellationToken cancellationToken = default)
        {
            return PlayShowAsync(
                target,
                options,
                default,
                UITransitionRollbackState.Hidden,
                null,
                false,
                cancellationToken);
        }

        public UniTask PlayHideAsync(
            RectTransform target,
            UITransitionOptions options,
            CancellationToken cancellationToken = default)
        {
            return PlayHideAsync(
                target,
                options,
                default,
                null,
                false,
                cancellationToken);
        }

        internal UniTask PlayShowAsync(
            RectTransform target,
            UITransitionOptions options,
            UIOperationId operationId,
            UITransitionRollbackState rollbackState,
            Type contextType,
            bool includeNavigation,
            CancellationToken cancellationToken)
        {
            return PlayAsync(
                target,
                options,
                true,
                operationId,
                rollbackState,
                contextType,
                includeNavigation,
                cancellationToken);
        }

        internal UniTask PlayHideAsync(
            RectTransform target,
            UITransitionOptions options,
            UIOperationId operationId,
            Type contextType,
            bool includeNavigation,
            CancellationToken cancellationToken)
        {
            return PlayAsync(
                target,
                options,
                false,
                operationId,
                UITransitionRollbackState.Visible,
                contextType,
                includeNavigation,
                cancellationToken);
        }

        public bool RequestInterruption(
            RectTransform target,
            UITransitionInterruption interruption)
        {
            if (_disposed || target == null ||
                !_targets.TryGetValue(target.GetInstanceID(), out var state) ||
                state.Active == null ||
                state.Active.IsReversing ||
                state.Active.Interruption.HasValue)
            {
                return false;
            }

            state.Active.Interruption = interruption;
            if (state.Active.IsCustom)
            {
                state.Active.Cancellation.Cancel();
            }

            return true;
        }

        public void CaptureBaseline(RectTransform target, bool overwrite = false)
        {
            ThrowIfDisposed();
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var state = GetOrCreateState(target);
            if (state.Active != null)
            {
                throw new InvalidOperationException(
                    "Cannot capture a transition baseline while the view is animating.");
            }

            if (!state.HasBaseline || overwrite)
            {
                var group = GetOrAddCanvasGroup(target);
                state.Baseline = new VisualState(
                    group.alpha,
                    target.localScale,
                    target.anchoredPosition);
                state.HasBaseline = true;
            }
        }

        public void NormalizeVisible(RectTransform target)
        {
            if (_disposed || target == null)
            {
                return;
            }

            if (!_targets.TryGetValue(target.GetInstanceID(), out var state))
            {
                return;
            }

            EnsureBaseline(state);
            Apply(state, state.Baseline, state.Generation);
        }

        public void Forget(RectTransform target)
        {
            if (target == null)
            {
                return;
            }

            if (_targets.TryGetValue(target.GetInstanceID(), out var state))
            {
                _targets.Remove(target.GetInstanceID());
                Retire(state);
                state.Active?.Cancellation.Cancel();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            var states = new List<TargetState>(_targets.Values);
            _targets.Clear();
            foreach (var state in states)
            {
                Retire(state);
            }

            _customTransitions.Clear();
            List<Exception> cancellationErrors = null;
            foreach (var state in states)
            {
                try
                {
                    state.Active?.Cancellation.Cancel();
                }
                catch (Exception exception)
                {
                    cancellationErrors ??= new List<Exception>();
                    cancellationErrors.Add(exception);
                }
            }

            if (cancellationErrors != null)
            {
                throw new AggregateException(
                    "One or more transition cancellation callbacks failed during disposal.",
                    cancellationErrors);
            }
        }

        private async UniTask PlayAsync(
            RectTransform target,
            UITransitionOptions options,
            bool isShow,
            UIOperationId operationId,
            UITransitionRollbackState rollbackState,
            Type contextType,
            bool includeNavigation,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (target == null || options == null || options.Type == UITransitionType.None)
            {
                return;
            }

            var settings = options.Snapshot();
            var state = GetOrCreateState(target);
            if (state.Active != null)
            {
                throw new InvalidOperationException(
                    $"View {target.name} already has an active transition writer.");
            }

            EnsureBaseline(state);
            if (ReducedMotion)
            {
                Apply(state, isShow ? state.Baseline : ResolveRollback(state, settings, UITransitionRollbackState.Hidden), state.Generation);
                return;
            }
            var generation = ++state.Generation;
            var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var session = new TransitionSession(operationId, linked)
            {
                IsCustom = settings.Type == UITransitionType.Custom,
                ContextType = contextType,
                IncludeNavigation = includeNavigation
            };
            state.Active = session;

            try
            {
                if (settings.Type == UITransitionType.Custom)
                {
                    await PlayCustomAsync(state, settings, isShow, rollbackState, session);
                }
                else
                {
                    await PlayBuiltInAsync(
                        state,
                        settings,
                        isShow,
                        rollbackState,
                        session,
                        generation);
                }
            }
            catch
            {
                if (state.Generation == generation && target != null)
                {
                    Apply(
                        state,
                        ResolveRollback(state, settings, rollbackState),
                        generation);
                }

                throw;
            }
            finally
            {
                if (ReferenceEquals(state.Active, session))
                {
                    state.Active = null;
                }

                linked.Dispose();
            }
        }

        private async UniTask PlayCustomAsync(
            TargetState state,
            UITransitionOptions settings,
            bool isShow,
            UITransitionRollbackState rollbackState,
            TransitionSession session)
        {
            if (string.IsNullOrWhiteSpace(settings.CustomTransitionId) ||
                !_customTransitions.TryGetValue(settings.CustomTransitionId, out var custom))
            {
                throw new KeyNotFoundException(
                    $"Custom UI transition '{settings.CustomTransitionId}' is not registered.");
            }

            try
            {
                using (UIOperationReentrancyScope.Enter(
                           session.ContextType ?? state.Target.GetType(),
                           session.IncludeNavigation))
                {
                    if (isShow)
                    {
                        await custom.PlayShowAsync(
                                state.Target,
                                settings,
                                session.Cancellation.Token)
                            .AttachExternalCancellation(session.Cancellation.Token);
                    }
                    else
                    {
                        await custom.PlayHideAsync(
                                state.Target,
                                settings,
                                session.Cancellation.Token)
                            .AttachExternalCancellation(session.Cancellation.Token);
                    }
                }

                if (session.Interruption.HasValue)
                {
                    throw new OperationCanceledException(session.Cancellation.Token);
                }

                if (isShow)
                {
                    Apply(state, state.Baseline, state.Generation);
                }
            }
            catch (OperationCanceledException) when (session.Interruption.HasValue)
            {
                var interruption = session.Interruption.Value;
                var destination = interruption == UITransitionInterruption.SkipToEnd
                    ? ResolveEndpoint(state, settings, isShow)
                    : ResolveRollback(state, settings, rollbackState);
                Apply(state, destination, state.Generation);
                if (interruption == UITransitionInterruption.SkipToEnd)
                {
                    return;
                }

                throw new UITransitionInterruptedException(interruption, session.OperationId);
            }
        }

        private async UniTask PlayBuiltInAsync(
            TargetState state,
            UITransitionOptions settings,
            bool isShow,
            UITransitionRollbackState rollbackState,
            TransitionSession session,
            long generation)
        {
            var destination = ResolveEndpoint(state, settings, isShow);
            var rollback = ResolveRollback(state, settings, rollbackState);
            var start = isShow ? ResolveEndpoint(state, settings, false) : state.Baseline;
            Apply(state, start, generation);

            var duration = isShow ? settings.ShowDuration : settings.HideDuration;
            var elapsed = 0f;
            var reversing = false;
            while (true)
            {
                session.Cancellation.Token.ThrowIfCancellationRequested();
                if (session.Interruption.HasValue)
                {
                    var interruption = session.Interruption.Value;
                    if (interruption == UITransitionInterruption.SkipToEnd)
                    {
                        Apply(state, destination, generation);
                        return;
                    }

                    if (interruption == UITransitionInterruption.Interrupt)
                    {
                        Apply(state, rollback, generation);
                        throw new UITransitionInterruptedException(
                            interruption,
                            session.OperationId);
                    }

                    reversing = true;
                    session.IsReversing = true;
                    start = ReadCurrent(state);
                    destination = rollback;
                    duration = Mathf.Max(0.001f, duration * Mathf.Clamp01(1f - elapsed / duration));
                    elapsed = 0f;
                    session.Interruption = null;
                }

                if (elapsed >= duration)
                {
                    break;
                }

                var delta = _clock.GetDeltaTime(settings.IgnoreTimeScale);
                if (!float.IsNaN(delta) && !float.IsInfinity(delta) && delta > 0f)
                {
                    elapsed += delta;
                    var linear = duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
                    var progress = Evaluate(settings.Curve, linear);
                    Apply(state, VisualState.Lerp(start, destination, progress), generation);
                }

                if (elapsed < duration)
                {
                    await _clock.NextFrameAsync(session.Cancellation.Token);
                }
            }

            Apply(state, destination, generation);
            if (reversing)
            {
                throw new UITransitionInterruptedException(
                    UITransitionInterruption.Reverse,
                    session.OperationId);
            }
        }

        private static float Evaluate(AnimationCurve curve, float value)
        {
            if (curve != null && curve.length > 0)
            {
                var evaluated = curve.Evaluate(value);
                return float.IsNaN(evaluated) || float.IsInfinity(evaluated)
                    ? value
                    : evaluated;
            }

            var inverse = 1f - value;
            return 1f - inverse * inverse * inverse;
        }

        private TargetState GetOrCreateState(RectTransform target)
        {
            var id = target.GetInstanceID();
            if (!_targets.TryGetValue(id, out var state) || state.Target == null)
            {
                state = new TargetState(target);
                _targets[id] = state;
            }

            return state;
        }

        private static void EnsureBaseline(TargetState state)
        {
            if (state.HasBaseline)
            {
                return;
            }

            var group = GetOrAddCanvasGroup(state.Target);
            state.Baseline = new VisualState(
                group.alpha,
                state.Target.localScale,
                state.Target.anchoredPosition);
            state.HasBaseline = true;
        }

        private static VisualState ResolveEndpoint(
            TargetState state,
            UITransitionOptions settings,
            bool visible)
        {
            if (visible)
            {
                return state.Baseline;
            }

            var hidden = state.Baseline;
            switch (settings.Type)
            {
                case UITransitionType.Fade:
                    hidden.Alpha = 0f;
                    break;
                case UITransitionType.Scale:
                    hidden.Scale = state.Baseline.Scale * settings.StartScale;
                    break;
                case UITransitionType.SlideLeft:
                    hidden.Position += new Vector2(-settings.SlideDistance, 0f);
                    break;
                case UITransitionType.SlideRight:
                    hidden.Position += new Vector2(settings.SlideDistance, 0f);
                    break;
                case UITransitionType.SlideUp:
                    hidden.Position += new Vector2(0f, settings.SlideDistance);
                    break;
                case UITransitionType.SlideDown:
                    hidden.Position += new Vector2(0f, -settings.SlideDistance);
                    break;
            }

            return hidden;
        }

        private static VisualState ResolveRollback(
            TargetState state,
            UITransitionOptions settings,
            UITransitionRollbackState rollbackState)
        {
            return ResolveEndpoint(
                state,
                settings,
                rollbackState == UITransitionRollbackState.Visible);
        }

        private static VisualState ReadCurrent(TargetState state)
        {
            var group = GetOrAddCanvasGroup(state.Target);
            return new VisualState(
                group.alpha,
                state.Target.localScale,
                state.Target.anchoredPosition);
        }

        private void Apply(TargetState state, VisualState visual, long generation)
        {
            if (_disposed ||
                state.Retired ||
                state.Target == null ||
                state.Generation != generation ||
                !_targets.TryGetValue(state.Target.GetInstanceID(), out var current) ||
                !ReferenceEquals(current, state))
            {
                return;
            }

            GetOrAddCanvasGroup(state.Target).alpha = visual.Alpha;
            state.Target.localScale = visual.Scale;
            state.Target.anchoredPosition = visual.Position;
        }

        private static void Retire(TargetState state)
        {
            state.Retired = true;
            state.Generation++;
        }

        private static CanvasGroup GetOrAddCanvasGroup(RectTransform target)
        {
            var group = target.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = target.gameObject.AddComponent<CanvasGroup>();
            }

            return group;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(UITransitionRunner));
            }
        }

        private sealed class TargetState
        {
            public TargetState(RectTransform target)
            {
                Target = target;
            }

            public RectTransform Target { get; }
            public bool HasBaseline;
            public VisualState Baseline;
            public long Generation;
            public TransitionSession Active;
            public bool Retired;
        }

        private sealed class TransitionSession
        {
            public TransitionSession(
                UIOperationId operationId,
                CancellationTokenSource cancellation)
            {
                OperationId = operationId;
                Cancellation = cancellation;
            }

            public UIOperationId OperationId { get; }
            public CancellationTokenSource Cancellation { get; }
            public UITransitionInterruption? Interruption;
            public bool IsCustom;
            public bool IsReversing;
            public Type ContextType;
            public bool IncludeNavigation;
        }

        private struct VisualState
        {
            public VisualState(float alpha, Vector3 scale, Vector2 position)
            {
                Alpha = alpha;
                Scale = scale;
                Position = position;
            }

            public float Alpha;
            public Vector3 Scale;
            public Vector2 Position;

            public static VisualState Lerp(
                VisualState from,
                VisualState to,
                float progress)
            {
                return new VisualState(
                    Mathf.LerpUnclamped(from.Alpha, to.Alpha, progress),
                    Vector3.LerpUnclamped(from.Scale, to.Scale, progress),
                    Vector2.LerpUnclamped(from.Position, to.Position, progress));
            }
        }
    }
}
