using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace YUIFramework
{
    public enum UITransitionInterruption
    {
        Interrupt,
        Reverse,
        SkipToEnd
    }

    public enum UITransitionRollbackState
    {
        Visible,
        Hidden
    }

    [Flags]
    public enum UIVisibilityState
    {
        None = 0,
        Visible = 1 << 0,
        Interactable = 1 << 1,
        Covered = 1 << 2,
        Suspended = 1 << 3
    }

    public interface IUITransitionClock
    {
        float GetDeltaTime(bool ignoreTimeScale);

        UniTask NextFrameAsync(CancellationToken cancellationToken);
    }

    public sealed class UnityUITransitionClock : IUITransitionClock
    {
        public float GetDeltaTime(bool ignoreTimeScale)
        {
            return ignoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;
        }

        public UniTask NextFrameAsync(CancellationToken cancellationToken)
        {
            return UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
        }
    }

    public sealed class UITransitionInterruptedException : OperationCanceledException
    {
        public UITransitionInterruptedException(
            UITransitionInterruption interruption,
            UIOperationId operationId)
            : base($"UI transition operation {operationId} was {interruption}.")
        {
            Interruption = interruption;
            OperationId = operationId;
        }

        public UITransitionInterruption Interruption { get; }
        public UIOperationId OperationId { get; }
    }
}
