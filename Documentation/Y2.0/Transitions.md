# Y2.0 Transitions and Visibility (Stage 8)

Stage 8 makes visual transitions interruptible without weakening the stage 2 lifecycle
state machine or the stage 3 FIFO lanes. Lifecycle callbacks remain serialized. An
interruption changes only the visual phase that is currently awaited; rollback or commit
still finishes before the next queued command executes.

## Production entry points

`UIConfig` selects a built-in fade, scale, or slide transition, or a registered custom
transition. `IUIService.Transitions.RegisterCustom(id, transition)` registers an
`IUITransition` without adding a package dependency. DOTween can be used behind such an
adapter, but YUIFramework has no DOTween dependency.

The active visual phase can be signaled with:

```csharp
ui.RequestTransitionInterruption<MyPage>(UITransitionInterruption.Reverse);
ui.RequestTransitionInterruption(page, UITransitionInterruption.SkipToEnd);
ui.RefreshTransitionBaseline(page);
```

The methods return `false` when no transition is active, another interruption already owns
the session, or shutdown has stopped public work. Their effect is observed on the next
transition update.

- `Interrupt` restores the operation's declared rollback visual and cancels the operation.
- `Reverse` continues from the current alpha/scale/position toward that rollback visual,
  then cancels the operation so its existing lifecycle rollback runs.
- `SkipToEnd` applies the current operation's destination and lets it commit.

For a refresh of an already-open context, the rollback visual is visible. For a new,
pooled, or hidden open it is hidden. A canceled close always rolls back to visible and
resets `CloseDisposition` to `None`.

## Single writer and generation fence

`UITransitionRunner` owns one session per `RectTransform`. A second writer is rejected.
Every built-in write checks the session generation and verifies that its exact target-state
object is still the current registry entry. Forget and disposal remove and permanently
retire the old state before cancellation, so a delayed cancellation continuation cannot
change a rebound or released view or clear its new writer.

Disposal invalidates the complete registry before invoking cancellation callbacks. Callback
failures are collected per session so one bad callback cannot prevent the remaining sessions
from being canceled; an aggregate is reported after the full sweep.

The default remains per-context-type FIFO. Different types can animate concurrently.
Calling an interruption method never invokes `OnShow`, `OnHide`, `OnClose`, or `OnDestroy`
and never bypasses a lane.

## Baseline and stable visuals

The runner captures the stable `CanvasGroup.alpha`, `localScale`, and
`anchoredPosition`. Fade returns to the captured alpha rather than hard-coding `1`;
scale is relative to the captured scale; slide is relative to the captured position.
Completed hides hold their hidden endpoint until the view is deactivated, then the
framework restores the stable baseline before pooling. This avoids drift across reuse.

New bindings capture after `OnInit`. Pooled bindings refresh at a stable, inactive point
when `UIConfig.RefreshTransitionBaselineOnReuse` is true. Layout or binding code that
intentionally changes the stable visual can call `RefreshTransitionBaseline` while no
transition is active. Capturing during an animation is rejected.

`UITransitionOptions` are copied at operation start. The `AnimationCurve` keys and wrap
modes are cloned, so changing a config or curve does not alter an in-flight operation.
Null or empty curves use the framework ease-out curve. Negative, infinite, and NaN
durations normalize to zero. The default uses unscaled time and therefore continues while
`Time.timeScale == 0`; an `IUITransitionClock` can be injected through the `UIManager`
constructor for deterministic tests.

## Custom transitions

Custom `IUITransition` implementations receive the immutable operation snapshot and linked
cancellation token. The runner uses external cancellation so shutdown and interruption do
not wait forever for an implementation that ignores its token, then restores the declared
stable visual.

A custom implementation still receives the real `RectTransform`. If it retains that
reference and writes after its returned task was externally canceled, the framework cannot
physically prevent that non-cooperative write. Adapters must observe cancellation and must
not continue writing after their task completes. Built-in transitions do not have this
limitation.

## Visibility and suspension

`BaseContext.VisibilityState` is a flags value. The observable flags are independent:

- `Visible`: the view is active and published by `UIManager`.
- `Interactable`: stage 4's `UIInteractionController` currently permits raycasts/focus.
- `Covered`: navigation or the active modal covers the context.
- `Suspended`: framework-managed refresh work should pause.

`UIInteractionController` remains the only writer of actual raycaster eligibility and
publishes its result back to the context. Navigation derives coverage from the final stack
after each transaction. Non-top pages retain the established hidden-page policy, and
`UIConfig.SuspendWhenCovered` controls whether their managed refresh work is suspended.
`WaitUntilResumedAsync` is a cooperative gate linked to the display and lifetime tokens;
it does not pause arbitrary tasks, coroutines, or user `Update` methods.

Navigation rollback recomputes coverage from the reconciled stack, so Push, Pop, Replace,
and BringToTop cannot leave stale suspension after cancellation or failure. Pooling and
release clear all visibility flags. Prewarm never starts a display scope or transition.
