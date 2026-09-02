# Y2 Navigation and Concurrency (Stage 3, Stage 4 Integration)

Stage 3 gives UI operations FIFO ordering per key, gives the page navigator its own
FIFO transaction queue, and defines a conservative, testable single-flight merge
policy for concurrent `OpenAsync<T>` calls. Stage 4 preserves those transaction rules and
adds sorting/modal/focus state through the same lifecycle calls.

## Per-key coordination (`UIOperationCoordinator`)

`UIManager` owns one internal `UIOperationCoordinator` (created fresh by each
`Initialize`/`InitializeAsync` call). It keys a FIFO lane by `Type` (the registered
context type, which is what `UIConfig.Key` / `UIKey` identifies one-to-one). `OpenAsync<T>`,
`CloseAsync<T>`, `CloseAsync(BaseContext)`, and the internal `HideCoreAsync`/`ShowCoreAsync`
used by the navigator all route through the lane for that context's `Type`. Different
types run on independent lanes and never block each other; PlayerLoop-driven work for
unrelated UI types genuinely proceeds concurrently.

A lane is a plain queue with one worker loop; the worker is only started when the first
command arrives and the lane is removed from the coordinator as soon as it drains, under
the same lock used to enqueue new work, so a command that arrives at the exact moment the
worker is deciding to retire cannot be silently dropped or orphaned. Every command's
`Func<CancellationToken, UniTask>` body runs inside a try/catch that funnels its result,
fault, or cancellation into the command's own `TaskCompletionSource`; the worker loop
itself can never fault, and a `.Forget(handler)` on its startup call guarantees any truly
unexpected defect is logged instead of becoming an unobserved task exception.

### Ordering guarantees

- Rapid `Open` followed by `Close` on the same type closes whatever the `Open` actually
  produced, because the generic `Close` resolves "the active context for this type" at
  the moment the command **executes**, not when it was enqueued.
- `Close(context)` re-validates that `context` is still the active instance for its type
  when it executes; a stale reference (for example an old `UIHandle<T>`) is a no-op even
  if a newer instance of the same type has since become active.
- `Close` then `Open` on the same type is strict FIFO: the close fully finishes (release
  or pool) before the next open begins.
- Navigator-driven `Hide`/`Show` (used internally by Push/Pop/Replace/BringToTop) run on
  the *same* per-type lane as `Open`/`Close`, through `UIManager.HideCoreAsync` /
  `UIManager.ShowCoreAsync`. They no longer bypass the lane with a synchronous call, so
  they cannot race a concurrent `Open`/`Close` for the same context.

### Single-flight `Open` merging

Two concurrent `OpenAsync<T>` calls for the same type share a single creation only when
**all** of the following hold at the moment the second call arrives:

1. The lane's currently running command is itself an `Open`.
2. That running `Open` resolved, at the instant it started executing, to a genuinely new
   instance — no active context and no pooled instance existed for the type. Refreshing
   an already-active context, or reactivating a pooled one, is never eligible: merging
   those would silently drop callback semantics (a real caller expects its own
   `OnShow`/callback timing) for a caller who never actually asked for a refresh.
3. The requested `args` are equal to the running command's `args` (`Equals`; both `null`
   counts as equal).
4. Nothing else is already queued behind the running command for that type. A merge never
   lets a later request jump ahead of something already waiting in line.

When all four hold, the second (and any further) caller shares the *same* result rather
than issuing a second `Activator.CreateInstance`/resource load/`OnInit`/`OnShow`. This is
implemented by boxing the shared execution into a `System.Threading.Tasks.Task` (not a
raw `UniTask`, which cannot safely be awaited more than once) that every attached caller
awaits independently.

Every caller, including the first, owns only its wait cancellation. The shared creation is
driven by the service lifetime plus an internal waiter-counted cancellation source:
cancelling one caller abandons only that wait while any other waiter remains; if every
waiter cancels, the coordinator cancels the now-unobserved shared creation so its normal
stage 2 rollback can release the partial instance/resource. Service shutdown always
cancels the shared creation. The next unrelated request is unaffected after that execution
has completed.

Close, Hide, and Show never merge; only a first-creation `Open` is a merge candidate, and
only while it is still running.

### Reentrancy vs. deadlock

Lifecycle callbacks and the synchronous invocation window of a navigation guard run inside
an explicit thread-local key scope. If a callback (`OnShow`, `OnHide`, `OnClose`,
`OnDestroy`) or guard synchronously calls back into `UIManager`/`UINavigator` for that key,
the call fails immediately with
`UIOperationReentrancyException` instead of enqueuing a command that could only ever run
after the very command it is nested inside of finishes — i.e. instead of deadlocking by
waiting on itself.

The stage 2 per-context guard (`UIOperationInProgressException`, thrown by
`BaseContext.BeginOperation` when a second operation starts while one is already active on
that context) is unchanged and remains in place as a last-resort safety net. In normal
operation the coordinator's FIFO lane and callback/guard reentrancy scope are what
prevent two operations from ever running concurrently on the same context, so
`UIOperationInProgressException` is not expected to fire through the public API surface
any more; it stays as defense-in-depth for any internal code path that might call
`BeginContextOperation` outside the coordinator in the future. The stage 2
characterization test for this (`OperationInProgress_FailsFastUntilCurrentOperationFinishes`)
was intentionally replaced by deterministic, gate-controlled FIFO/merge tests in
`UIOperationCoordinatorCharacterizationTests`, per the stage 3 characterization-test rule.

## Shutdown ordering

`UIManager.ShutdownAsync`:

1. Atomically rejects new public UI operations and stops the navigator from accepting new
   commands, then cancels the service lifetime token.
2. Already-accepted navigation commands observe cancellation and finish their normal
   rollback while the per-key UI coordinator remains available. This ordering is required
   so a canceled Push can re-show the page it hid instead of having rollback rejected by a
   prematurely stopped UI lane.
3. After the navigator drains, stops and drains the per-key UI coordinator, then enables
   shutdown-only lifecycle cleanup and awaits the existing in-flight-operation count as an
   independent check.
4. Only then does it perform the existing `allowDuringShutdown` cleanup (closing
   remaining active contexts, clearing pools, navigation, and messaging) by calling
   `CloseInternalAsync` directly — this cleanup step never enqueues onto the
   now-stopped coordinators.
5. `Initialize`/`InitializeAsync` always construct a brand-new `UIOperationCoordinator`
   (and, through `new UINavigator(this)`, a brand-new navigator-owned coordinator), so a
   later `Initialize` after a successful shutdown starts with clean, unstopped queues.

## Navigator FIFO transactions

`UINavigator` owns one more `UIOperationCoordinator` with a single fixed lane key, so
`PushAsync`, `PopAsync`, `ReplaceAsync`, `BackAsync`/`NavigateBackAsync`, and the new
public `BringToTopAsync<T>` all serialize on one FIFO queue. `IsBusy` reports whether that
queue currently has a running or queued command. `NavigateBackAsync` is a plain alias for
`BackAsync`; existing callers of `BackAsync` keep working unchanged.

### Duplicate prevention

Pushing (or replacing onto) a type that is already somewhere in the stack **never**
creates a second entry, regardless of `UINavigateOptions.BringExistingPageToTop` — that
option no longer has any effect and remains only for API compatibility.
Two special cases:

- If the existing entry is already the top of the stack, `Push`/`Replace` treat it as a
  **refresh only**: `OpenAsync<T>` runs again (so `OnShow` fires with the new args) and
  the instance is never closed or duplicated.
- If the existing entry is somewhere else in the stack, `Push`/`Replace` both delegate to
  the same bring-to-top behavior as the public `BringToTopAsync<T>`: the target is shown
  and every page above it is closed, regardless of `CloseCurrentPageOnReplace` for the
  `Replace` case. This is a deliberate simplification for a case that combines two
  different navigation intents; document it if your product surfaces `Replace` onto an
  existing-elsewhere page as a distinct user action.

### Full-screen stack policy (unchanged from phase 0, made explicit)

At any point in time, only the top stack entry should be `Opened`; every other tracked
entry should be `Hidden`. Hidden stack entries retain bounded sorting leases but are not raycast-eligible. Showing a
previous entry reuses and brings forward its lease; closing/pooling releases it. Modal
contexts use a separate global modal stack and do not alter the page-stack identity rules.
`UIPageStackEntry.FullScreen` remains reserved. The legacy
`UINavigateOptions.HideCurrentPage` property is retained for source compatibility but no
longer changes behavior.

### Guards

`IUINavigator.Guard` is an optional `UINavigationGuard` delegate:

```csharp
public delegate UniTask<bool> UINavigationGuard(UINavigationRequest request, CancellationToken cancellationToken);
```

`UINavigationRequest` carries `Kind` (`Push`/`Pop`/`Replace`/`BringToTop`; `Back` is
represented as `Pop` because it is implemented as one), `FromType` (current top before the
command), `ToType` (the command's target, or the page that would become the new top for a
Pop), and `Args`.

The guard is evaluated exactly once per command, at **execution** time (after the command
reaches the front of the navigator's FIFO queue, using a snapshot of the stack at that
moment), and strictly before any stack mutation or context lifecycle call:

- A refusal (`false`) has no side effects. For `Push`/`Replace`/`BringToTop` it throws
  `UINavigationRejectedException`. For `Pop`/`Back` it does not throw:
  `PopAsync` returns the (unchanged) current page and `BackAsync`/`NavigateBackAsync`
  return `false`, matching their existing "nothing to do" contract.
- An exception thrown by the guard itself propagates unchanged (it is never wrapped) and
  the stack remains exactly as it was.
- A cancelled guard token propagates as `OperationCanceledException`.
- Because the guard runs on the navigator's single FIFO lane, a slow guard naturally
  serializes every subsequent navigation command behind it — this is by design, not a bug.

**Reentrancy limitation:** calling any navigator method (`PushAsync`, etc.) from inside a
lifecycle callback (`OnShow`/`OnHide`/...) that is itself running as part of the
navigator's *current* command fails immediately with `UIOperationReentrancyException`
rather than deadlocking. A common instance of this is "redirect on show" (a page's
`OnShow` immediately wants to push a different page): that pattern is not supported
synchronously in stage 3. Schedule the follow-up navigation for the next frame (or the
next `UniTask.Yield`) instead of calling it directly from inside the callback.

### Transaction ordering and destructive-failure convergence

Every command snapshots the stack and evaluates the guard at execution time, then follows
a non-destructive-first order so a failure before anything destructive happens can be
cleanly undone:

| Command | Order | Rollback on failure before anything destructive |
|---|---|---|
| Push (new page) | hide current (if any) → open target | re-show the current page; if that also fails, the two failures are combined into an `AggregateException` |
| Push/Replace (existing, not top) | delegates to BringToTop: show target → close pages above | see BringToTop row |
| Pop/Back | show previous → close current | re-hide the previous page, leaving current on top exactly as before |
| Replace (new page) | open target → close/hide current | close the just-opened target again, leaving the stack untouched |
| BringToTop (public or via Push/Replace) | show target → close pages above | n/a (see convergence below; closing pages above is the destructive step) |

Rollback cleanup is cancellation-immune once it starts. In particular, Shutdown first
cancels the failed command but keeps the UI-key lanes available while the navigator closes
any just-opened replacement target, including its hide transition, before those lanes drain.

**Once a destructive close has actually released (or pooled) an identity, the navigator
never fabricates or reopens a replacement for it.** Instead it converges the tracked stack
to a minimal, consistent state:

- If `Pop`/`Back`'s close of the current page fails *after* it was force-released, the
  entry is dropped and the previous page (already shown) becomes the converged top.
- If `Replace`'s close/hide of the current page fails after the new page already opened
  successfully, the old entry is dropped and the newly opened page becomes the converged
  top; if the old page is *not* released (for example the failure happened before any
  destructive step), the new page is closed again instead and the stack is left exactly
  as it was.
- `BringToTop` always attempts to close every page above the target, even if one of them
  fails, then rebuilds the stack from what actually survived. A page above the target
  that failed to close is dropped from the tracked stack — it can no longer be reached
  through `Back`/`BringToTop`. **This is a documented, degraded-but-consistent outcome,
  not a fully atomic one**: the target ends up on top and shown, but a failed-to-close
  page above it is orphaned outside the tracked stack rather than either being cleanly
  closed or kept reachable.

After every command, on both the success and failure paths, the navigator also runs a
structural safety net that removes any stack entry whose context has already reached
`Released` or `Pooled` — the stack is never left pointing at an entry that is no longer
registered as active. If that safety net itself throws, its exception is combined with the
original failure into an `AggregateException` rather than silently swallowed.

## Known limitations

- `Replace` onto an existing-elsewhere page always behaves like `BringToTop` (closes
  everything above it) regardless of `CloseCurrentPageOnReplace`; there is no separate
  "replace in place while keeping the rest of the stack" mode for that combination.
- Redirect-on-show (calling a navigator method from inside the current command's own
  lifecycle callback) is not supported; it fails fast via `UIOperationReentrancyException`
  instead of deadlocking. See "Reentrancy limitation" above.
- Multi-page destructive failures (BringToTop closing several pages above the target) are
  degraded-but-consistent, not atomic: a page that fails to close is dropped from the
  tracked stack rather than retried or kept reachable.
- A caller waiting for a command that is still sitting in a lane's pending queue (not yet
  the one actually executing) cannot cancel that wait early; cancellation is observed once
  the command starts executing and its token is checked by the underlying operation (for
  example inside a resource load), not merely because it is queued. Only a merge-attached
  waiter (see the single-flight `Open` policy above) has an independent, always-immediate
  per-caller cancellation while the shared command is running.
- Stage 4 interaction state follows lifecycle transaction outcomes; it does not make the
  documented multi-page destructive-failure convergence atomic.
