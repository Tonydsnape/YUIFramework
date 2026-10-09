# YUIFramework Y2.0 MVVM and Binding Lifecycle (Stage 9)

Stage 9 extends the existing code-first MVVM layer. It does not use runtime reflection,
assembly scanning, or a third-party MVVM/message framework.

## Observable and validated properties

`IReadOnlyObservableProperty<T>` exposes value observation without mutation.
`IObservableProperty<T>` adds `Value` and `SetValueWithoutNotify`.
`ObservableProperty<T>` implements both and suppresses equal-value notifications.
`ReadOnlyObservableProperty<T>` provides an explicit public read-only view.

`ValidatedProperty<T>` runs an injected typed validator and exposes `IsValid`,
`ValidationError`, and `ValidationChanged`. Validation remains model state; bindings only
render it.

## Commands

`IUICommand`, `UICommand`, and `UIAsyncCommand` expose:

- `CanExecute`, `IsExecuting`, `LastError`, and `StateChanged`;
- an awaitable `ExecuteAsync(CancellationToken)`;
- explicit `NotifyCanExecuteChanged`; and
- idempotent disposal.

An async command rejects concurrent execution, links caller cancellation with command
lifetime, and always clears busy state in `finally`. Synchronous exceptions, asynchronous
exceptions, and cancellation remain observable to the awaiting caller. State observers are
invoked independently; their failures are aggregated only after command state and execution
have reached a stable result. If the initial busy-state notification disposes/cancels the
command, the linked token is checked before invoking the business delegate, so no new side
effect starts after disposal. Disposal cancels active cooperative work. Button binding checks
`CanExecute` again on click, disables while busy, links binding disposal to cancellation, and
observes/logs the returned task instead of using an unhandled `async void`.

## Control bindings

`UIDataBinding` supports:

- `Text` and `TMP_Text`, with typed formatters;
- `Toggle`, `Slider`, and `Scrollbar`;
- `InputField` and `TMP_InputField`, with typed formatter/converter pairs and optional
  validation text;
- `Dropdown` and `TMP_Dropdown`; and
- `Button` plus `IUICommand`.

Two-way controls use Unity's `SetValueWithoutNotify` equivalent for model-to-view updates.
This prevents feedback while preserving initial synchronization and equal-value suppression
in the property. Binding callbacks treat destroyed Unity objects as absent. Disposing and
rebinding removes exact UnityEvent delegates, so listeners do not accumulate.

Formatting and some Unity control internals may allocate. Stage 9 measures both the pure
typed-message path and warmed typed `ObservableProperty<float/bool>` to real
`Slider`/`Toggle` no-notify updates. The latter has an explicit small Editor/Mono budget and
does not imply that formatted text, every Unity control, full UI frames, or device builds are
zero-allocation.

## ViewModel and binding ownership

`BaseContext.SetViewModel` accepts `UIViewModelOwnership`:

- `Owned` (default): replacement or terminal `OnDestroy` disposes the ViewModel.
- `External`: the context detaches but never disposes the borrowed ViewModel.

Replacing a ViewModel first disposes lifetime and display ViewModel bindings. Old command
listeners and old async binding cancellation therefore cannot write a newly rebound view.
Binding ownership is separate from ViewModel ownership:

- `TrackBinding` lasts until rebind or terminal destruction.
- `TrackDisplayBinding` lasts only for the active display cycle and is also cleared on rebind.
- ordinary display resources remain governed by stage 7's display scope.

Hide and pooling retain the current ViewModel and lifetime bindings for compatibility. A
successful terminal close/release disposes an owned ViewModel; an external one remains
caller-owned. A canceled stage 8 close that returns to `Opened` retains its display binding
scope. `Suspended` is a cooperative gate for framework-managed refresh work; it does not
pretend to pause arbitrary business tasks.

`BindingToken` and `ViewModelBase` attempt every registered cleanup and throw one aggregate
afterward, so a bad disposable cannot strand later subscriptions.
