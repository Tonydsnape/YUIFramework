# Stage 12: scene collaboration and system UI

These are instance-owned services over the existing Context lifecycle, per-type
FIFO, navigator FIFO, resource leases, pool identities and root input runtime.
There is no new singleton, resource backend, transition writer or network client.
All APIs, notifications and cancellation must run on the owning Unity thread.

## Run the sample

In a separate empty scene, attach `SystemUISample` to an empty GameObject and
enter Play. Do not run it alongside `GameBootstrap`/`HelloUIBootstrap`: each is an
independent composition root claiming one UIRoot/EventSystem. The existing
`Assets\Scenes\LoadScene.unity` login-cover startup is unchanged.

The sample loads Config first, maps the `system` UISettings profile through
`SampleUIConfiguration.Map`, then uses `RegisterBatch`. Five code-built views
use `CodeViewLoader`, the established minimal/demo path, not a production resource
fallback. Controls demonstrate two independently completed/cancelable loading
operations, 100 merged notifications, scene confirmation, real active-scene change
and global disconnected/maintenance/force-update notices. The sample creates
in-memory scenes only and unloads its own scenes at teardown.

`Config\tools\extend-system-ui-workbook.js` adds only missing system records to
the maintained workbook; it does not replace the original six records or existing
designer edits. Run the existing transactional `npm.cmd run export:client` from
`Config` after edits. JSON and MessagePack contain all eleven records. No schema,
generated public type or dependency was added.

The sample uses safe-area **content**, anchored panels, wrapping text, scrollable
vertical layouts, real Buttons/Slider and default keyboard/gamepad focus. Toast
graphics never receive raycasts. Loading uses its existing Loading modal layer;
notices use System above it. Only the Toast scheduler has a sample Update driver;
Loading/progress/dialog views update from events. Request text/action labels are
caller data: pass `TextLocalizationService` results for production. The sample's
English developer copy and builtin LegacyRuntime font are not a second localization
catalog or a claim that all system UI has been themed/localized.

## UI lifetime scopes reuse pool identities

```csharp
UIScope scene = ui.Scopes.CurrentScene;
UIScope module = ui.Scopes.CreateModule("Inventory");
await scene.PushAsync<InventoryPage>(args, token);
await module.OpenAsync<InventoryWidget>(args, token);
await scene.RunAsync(async ct =>
{
    using var asset = await resources.LoadAssetAsync<Sprite>("Icon", cancellationToken: ct);
    ct.ThrowIfCancellationRequested();
    // Consume on the owning thread; the operation owns this temporary lease.
}, token);
await module.ReleaseAsync();
```

`Global`, `ForScene(Scene)` and `CreateModule(name)` return lifetime owners whose
`Identity` is the **same `UIPoolScope`** used by manager open/prewarm and pool
diagnostics. Scene identity uses the real handle, never the name. `CurrentScene`
is a convenience lookup of the currently loaded active scene, not cached forever.
Default `OpenAsync` and navigation options remain Global for compatibility.
`UINavigateOptions.Scope` also supports scoped Push/Replace/BringToTop through
the same navigator transaction queue. `UIScope.PushAsync` supplies that option.

Ending a lifetime scope immediately marks it ended and cancels its tasks,
caller-linked work, in-flight Context operations and pool token. New work fails explicitly. It then queues
navigation pruning, drains affected UI lanes, destructively closes **active and
hidden** contexts, evicts idle instances, releases leases and drains tracked work.
Cleanup attempts every type even after cancellation callbacks or destroy callbacks
throw, and aggregates errors. Concurrent/repeated `ReleaseAsync` calls share the
same completion, including failure. Global ends only during manager shutdown.

`SceneManager.activeSceneChanged` ends the previous active scene owner even if the
old scene remains additively loaded. `sceneUnloaded` ends a non-active additive
scene owner too. This policy is deliberate: transfer UI explicitly to Global or
an independently owned Module if it must survive active-scene changes. Switching
back to a still-loaded scene creates a fresh owner. Unloading/reloading same-name
scenes cannot revive an earlier owner.

Events start asynchronous cleanup and log failures through `Debug.LogException`.
At a transition boundary retain and **await `oldScope.ReleaseAsync()`** before
declaring old-scene cleanup complete or disposing borrowed scene resources. A
same-type open in a new scope can hand off an **ending UI lifetime scope** on
the existing type lane; the old teardown cannot close its replacement. Two still
live scopes cannot own the same active Context type.

This does not change `ReleaseScopeAsync(UIPoolScope)`: that stage-7 API still
evicts **idle pool entries only**, leaving active UI alive and preventing its
future recaching. Do not use it as scene lifetime teardown. Use `UIScope.ReleaseAsync`
for active UI/tasks. This distinction preserves existing pooling callers.

`RunAsync` checks cancellation before invocation and after completion. A
noncooperative operation is drained, not forcibly aborted; it must eventually
finish and must own/clean its acquired resources in `using`/`finally`. Arbitrary
code that ignores its token can still make external writes; the framework cannot
undo them. Do not await release/shutdown from a task owned by that same scope
(self-join), or a lifecycle/guard callback that the release must drain.

## Reference-counted loading and progress

```csharp
using var operation = loading.Begin(scene, "Downloading", canCancel: true, weight: 2);
await DownloadAsync(operation, operation.Token); // IProgress<float> and cancellation
```

Each Begin returns an independent, idempotent `UILoadingOperation`; disposing or
canceling one cannot remove another. The handle's token links caller and owner.
Caller cancellation, scope end and Loading service disposal automatically remove
handles, including non-user-cancelable ones. User Cancel only affects operations
that opted in; `CancelCancelable` attempts them all. Dispose ends the handle and
cancels its token; normal completion is represented by completing work then Dispose,
not a separate success-shaped cancellation result.

`State.Count > 0` is the sole visibility condition. Progress is indeterminate until
every active handle has reported a finite value in 0..1; otherwise it is the
weighted mean of active handles. Completion/removal changes that denominator.
The newest active message is shown, reverting when its owner ends. Notifications
are event-driven, dispatch all observers and aggregate observer failures. A failed
Begin notification rolls back that admission; late reports on ended handles are
ignored. The sample presenter serializes open/close reconciliation and cancels a
pending open if the last handle ends.

## Bounded Toast scheduling

`UIToastService.Enqueue(owner, UIToastRequest)` returns a ticket whose multi-await
`Completion` yields an explicit outcome. Dispose a live ticket to cancel it.
Call Tick from one main-thread driver; the default clock is unscaled Unity time.
Tests inject `IUISystemClock` without sleeping.

| Default policy | Value / behavior |
|---|---|
| Capacity | 8 total active + pending items |
| Admission rate | 10 distinct accepted items per sliding 1-second window |
| Display | One active item, default duration 2 seconds |
| Minimum gap | 0.1 seconds between active items |
| Merge | Ordinal key **and scope identity**; increments a saturating count |
| Priority | Higher integer first; equal priority FIFO, no active preemption |
| Full queue | Higher-priority incoming item supersedes the oldest lowest-priority pending item; otherwise Overflow |
| Merge deadline | Never extends the original active deadline |

Merging returns an immediately completed `Merged` ticket, not another retained
waiter/cancellation registration. It cannot cancel the original ticket. This is
why 10,000 identical requests occupy one queue item rather than 10,000 subscribers.
RateLimited/Overflow/Superseded/Canceled/ServiceStopped are distinct terminal
outcomes; Displayed means the scheduler completed the active interval, not that
a renderer/GPU frame was proven visible. Admission is explicit, never silent.
Owner cancellation removes its active and pending items only. Dispose resolves
every retained ticket and detaches owner-token registrations. The clock must be
monotonic; zero-reference resource caches remain governed by the resource owner.

## Confirmation and application system policies

```csharp
var dialogs = new UIConfirmationService<MyConfirmationContext>(ui);
UIDialogResult result = await dialogs.ConfirmAsync(scene,
    new UIConfirmationRequest(title, message, confirmLabel, cancelLabel), token);
await dialogs.ShutdownAsync();
```

Use one confirmation service per registered confirmation Context type.
`UIConfirmationContext` is a template over `BaseDialogContext`; override HandleInit
for view creation, Present(session) for event bindings and DefaultFocus for the
primary button. Route buttons to `Session.Confirm()` / `Session.Decline()`. The
session accepts exactly one result, so double clicks and late references cannot
complete another request. Decline is unavailable when AllowCancel=false.

Requests are FIFO, including independently canceled queued requests. Confirmed and
Declined are explicit choices; successful external Hide/Close/Destroy is Dismissed.
Caller/scope/service cancellation propagates `OperationCanceledException`, not
Declined or a fabricated positive result. Canceled hide-transition rollback retains
Opened, DisplayToken and the unresolved session. Service completion waits for its
close cleanup; failures surface after cleanup attempts. Lifecycle-driven completion
defers cleanup outside the guarded callback and checks publication after a canceled
open wait. Cleanup is fenced to that session, not a pooled Context's later display.
Shared shutdown completion safely supports concurrent awaiters.

`UISystemUIService<TSystemContext>` fixes ownership to Global and supplies an
extension boundary, not a network SDK. Disconnected confirmation maps to Retry,
maintenance to Acknowledged, force-update to UpdateRequired; decline/external
dismiss maps to Dismissed. Maintenance and force-update require a non-declinable
request. They do **not** prevent an application owner from closing the view;
application policy must handle Dismissed and must not proceed as if confirmed.
No actual retry, authentication, store launch or server maintenance logic runs.

## Unified scene/UI input

Scene input must consult `ui.InputArbiter`, not independently toggle EventSystem:

```csharp
if (ui.InputArbiter.CanProcessScenePointer(touch.fingerId)) HandleSceneTouch(touch);
if (ui.InputArbiter.CanProcessSceneKeyboard()) HandleSceneKeys();
using (ui.InputArbiter.Acquire(this, "Scene transition", UILayer.Loading, UILayer.System))
    await SwitchSceneAsync(token);
```

The arbiter reads the **same** reference-counted InputLocks, modal stack, visible
fullscreen pages and EventSystem selection/pointer state. Nested whitelist locks
still intersect. Pointer IDs must match EventSystem touch IDs; -1 is the mouse
default. Keyboard gameplay is suppressed while UI owns selection. Non-modal Toast
cannot steal scene input. Scope operations should keep transition leases in `using`
so cancellation releases them. This is an opt-in gate for scene controllers; it
cannot intercept input from gameplay code that never consults it.

Escape/Android Back goes to the top modal before page navigation. An
`IUIBackHandler` can consume it; the standard confirmation maps Back to Declined
only when AllowCancel=true. Loading and a non-declinable notice block underlying
page navigation. A modal without a handler also blocks Back; no modal permits the
existing navigator Back route. Input locks still take precedence over both paths.

## Ownership and acceptance boundary

Stop event producers/presenters and confirmation queues, dispose Loading/Toast,
then await UI shutdown. Manager shutdown cancels/drains all lifetime scopes before
stopping the UI lanes and releasing the root. Dispose borrowed presentation/text/
Config/resources afterward in their established order. None of these system
services shuts down an injected resource/config/localization/presentation service.

The sample and core regressions live in `Stage12SystemUIEditModeTests` and
`Stage12SystemUIPlayModeTests`; [Testing.md](Testing.md) records the exact gates.
Stage 13 diagnostics/budgets, stage 14 build tooling, HybridCLR, real networking,
device/IL2CPP certification and FPS claims remain out of scope.
