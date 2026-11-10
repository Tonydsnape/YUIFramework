# Runtime Diagnostics and Performance Budgets (Stage 13)

Diagnostics is an optional **instance-owned observer**, not a second UI registry.
It reads the existing manager, navigator, modal stack, pool, resource service,
message center, input locks and transition runner. Runtime state remains
authoritative in those services. All inspection/recording APIs follow the
Unity-main-thread contract.

## Composition and ownership

Inject the **same** optional `UIDiagnostics` into `UIManager` and
`UIResourceService`. For production resources, register the existing
`YooAssetResourceProvider` with the verified Bootstrap package:

```csharp
#if UNITY_EDITOR || DEVELOPMENT_BUILD
var diagnostics = new UIDiagnostics(eventCapacity: 512, activeCapacity: 128);
#else
UIDiagnostics diagnostics = null;
#endif
var packages = new UIResourcePackageRegistry();
packages.Register(new YooAssetResourceProvider(readyPackage), isDefault: true);
var resources = new UIResourceService(packages, diagnostics);
var ui = new UIManager(diagnostics: diagnostics);
ui.Initialize(resources);
// Existing Config registration and application entry follow unchanged.
```

`readyPackage` is the application's already verified YooAsset ResourcePackage;
the provider type is in `YUIFramework.Bootstrap.YooAsset`. This does not install
a new loader or synchronously acquire production assets. Custom implementations
of `IUIResourceService` receive manager-side Load spans but must implement their
own native/lease observation if equivalent detail is needed. The legacy minimal
loader cannot expose native leases; its resource snapshot is explicitly absent.

Diagnostics, adapters and samplers are **borrowed**, not shut down by UIManager.
Keep diagnostics alive through UI and resource cleanup, then dispose the sampler,
adapter bindings and diagnostics. Disposing diagnostics stops new admissions;
it does not fabricate completion for outstanding work. UIManager can still be
shut down and reinitialized according to the existing contracts.

By default no diagnostics instance is constructed or injected. A null/disabled
observer adds no recording, clock reads on business hooks, automatic frame
sampling or hierarchy scans. It is not a claim of zero overhead for every
instrumented async method. Enabled recording allocates bounded event/span data;
snapshot/report/panel refresh intentionally allocate.

## Structured lifecycle and timing

`GetEvents()` returns an immutable chronological ring snapshot.
`GetOperations()` returns actual outstanding execution/lease spans. Each record
contains OperationId, ParentId, kind, page, stable instance ID, scope identity,
resource address, phase, monotonic duration, frame and exception chain.
Exception text is bounded to 8,192 characters; malformed exception formatters
cannot replace the business exception.

The correlation chain is explicit across awaits, not an ambient AsyncLocal:

```text
Navigate -> Open/Hide/Show/Close -> Load -> NativeLoad
                                  Load -> Lease (until actual release)
             Open/Close -> Transition
```

Context spans reuse the existing `UIContextOperation.Id`; navigation and nested
loads/transitions get distinct IDs. Unrelated concurrent direct opens are not
children of a currently executing navigation transaction. Equivalent merged
first opens retain one execution trace; per-waiter resource Load spans still
have one shared NativeLoad. NativeLoad records its terminal **before** publishing
the shared result, so inline consumers do not inflate native duration.

These are **execution durations**, excluding coordinator queue wait.
Transition duration is separate from loading and rendering. Begin/phase and one
terminal Completed/Canceled/Faulted record describe each admitted span. Normal
destruction cancels LifetimeToken as cleanup and still records a successful Close
or Release; actual cancellation/rollback records Canceled. Canonical state names
avoid the old Closed/Destroyed enum aliases in diagnostic text.

`UIScope.RunAsync` observes actual task completion. Ending a scope cancels its
token but a noncooperative task or native provider remains visibly pending until
it really finishes. NativeLoad identifies its **initiating** owner, not exclusive
ownership of a shared request. Inspect resource waiter/lease counts alongside it.
Diagnostics never aborts work, drains a queue, releases a lease or changes state.

The injected `IUIDiagnosticClock` uses Stopwatch timestamps by default.
Tests advance a fake monotonic clock to detect budget timeouts without sleeps.
A broken clock is isolated, warns once and marks coverage incomplete.

## Capacity, adapters and error isolation

Defaults are 512 events, 128 active spans and 128 terminal-owner audit entries.
The event ring overwrites the oldest record and increments `DroppedEvents`.
Active capacity or reentrant adapter recording rejects admission and increments
`DroppedOperations`; existing work continues. Terminal owner audits contain
values only, not strong context references. A wrapped event ring is a rolling
history, not proof that a complete older lifecycle is retained.

`Enabled = false` stops new admissions; already-running spans still terminate
exactly once. Disabled recording, dropped operations or clock faults produce
`RecordingIncomplete`. Performance reports do **not** certify zero owner residue
from an incomplete snapshot.

Implement `IUIDiagnosticsAdapter.Write(UIDiagnosticEvent)` and retain the token
returned by `Attach`. Call `FlushAdapters()` explicitly at an application-safe
boundary or through panel refresh. Business hooks never invoke external adapters,
including while holding a resource lock or performing lifecycle cleanup.
An adapter that throws is removed, warned and recorded as AdapterFault; later
adapters continue. Fault records are available on the following flush. Flush
reentrancy is ignored; adapters must not call business UI or record more spans
(such admissions are rejected and counted). There is no SDK, network transport,
telemetry upload, account/device unique ID or message-payload capture.
Applications must redact exception messages/addresses before external storage.

## Ownership and findings

`diagnostics.Capture(ui, budget, includeHierarchy: true)` must use the observer
injected into that manager. It returns copied metadata, never mutable internal
collections. It includes active/loading/hidden/pooled contexts, state, visibility,
layer/sorting, page and modal stacks, resources, leases, scopes, pool counters,
focus, locks, active transition generations, subscriptions, events and findings.
Hierarchy counting is opt-in and absent when the root is unavailable.

| Finding | Actual evidence |
|---|---|
| DuplicateSubscription | Same topic, payload type, owner **reference** and Delegate.Equals handler, not a count threshold |
| OwnerSubscriptionLeak | Subscription still registered for an ended Context/UIScope |
| OwnerLockLeak | Existing lock held by an ended Context/UIScope |
| OwnerLeaseLeak | Still-active explicitly owned lease span for an ended owner |
| OwnerWorkPending | Observed work remains active after its owner ends; not declared cleaned |
| OperationTimeout | Non-lease active duration exceeds the injected budget |
| RecordingIncomplete | Disabled recording, admission loss or clock failure |

Normal context cleanup already removes message tokens and owner registrations.
The runnable fault demo deliberately registers **after** destruction to simulate
a bad late callback; it does not weaken existing cleanup to manufacture a leak.
The terminal-owner audit records subscriptions/locks at destruction; a later
snapshot also detects subsequently added residue.

Use the concrete `UIResourceService.LoadOwnedAssetAsync<T>(context, key, scope,
token)` when a business asset must be associated with an owner. Registration
does not transfer cleanup ownership or automatically link cancellation: callers
must use their display/lifetime/scope token and release in using/finally.
Ordinary borrowed/global leases, legitimate live idle-pool leases, unreferenced
caches and TMP application-resident fonts are not labeled leaks. External tasks,
leases or third-party event systems without explicit registration are outside
coverage. Legacy string-message wrappers create closures; exact underlying
original-handler duplicate detection is supported for typed subscriptions only.

## Real profiler sampling and budgets

`UIProfilerSampler` owns and disposes native **ProfilerRecorder** instances.
It enumerates the current Unity runtime before starting a recorder, records
units and sample call counts, and uses the last completed sample (which may
predate capture for sparse markers). Invalid/unavailable/unwarmed counters have
`Value = null`, an explicit reason and Unavailable, never a fabricated zero.
Call `RefreshAvailableCounters()` after warmup to discover late-registered
markers; panel refresh does this on demand. No automatic per-frame enumeration.
`ObserveMarker(metric, marker)` adds a real enumerated marker, up to 16 total.

Unity 2022.3.62f2 Editor verification observes:

| Metric | Actual source / meaning |
|---|---|
| MainThread | Internal/Main Thread, ns converted to ms; **not** GPU or complete frame certification |
| GCAllocatedInFrame | Memory/GC Allocated In Frame, bytes across the sampled frame |
| CanvasRebuild | UI Render/Canvas.BuildBatch, ms and calls |
| LayoutRebuild | UI Layout/Layout, exact category disambiguates the generic name |
| DrawCalls / Batches | Render counters; real zero under Null graphics is not a device rendering result |
| Graphics / Raycast Targets | On-demand active uGUI hierarchy counts, not rebuild timings |
| MaxCompletedLoad | Max retained completed async Load duration, not frame/render time |
| CachedOpenToShow | PoolHit to CachedShowStarted in frames; excludes queue wait and transition completion |
| DestroyedOwnerResidue | Observed current residue only; unavailable when recording is incomplete |
| ProductionSynchronousLoads | Unavailable: an async facade cannot prove an arbitrary provider never blocks |

The default production YooAsset provider was source-audited: it calls
ResourcePackage.LoadAssetAsync and awaits the handle, without LoadAssetSync or
WaitForAsyncComplete. This is a bounded code audit, not a device trace or a new
stage-14 build checker. Custom providers require their own audit/Profiler trace.
Other Unity/platform builds may support different marker names or none.

Budgets are immutable, configurable `UIPerformanceBudget` values:

| Target | Main-thread reference | Canvas / Layout | GC target | Active Graphic / raycast |
|---|---:|---:|---:|---:|
| LowEndAndroid | 16.667 ms | 2 / 1 ms | 0 B/frame | 1500 / 200 |
| MainstreamMobile | 16.667 ms | 1.5 / .75 ms | 0 B/frame | 2500 / 400 |
| Editor | 16.667 ms | 8 / 4 ms | 0 B/frame | 4000 / 800 |

All default profiles use a 10,000 ms operation budget and <=1 frame cached-show
target. Supply a constructor-configured budget for the actual game/scenario.
`Report(budget, scenario, snapshot, declaredDeviceClass)` records actual Unity,
platform/model/graphics backend, frame, scene, scenario and every threshold.
Editor observations cannot Pass mobile budgets even with a declared class.
Device runs require an explicit device class and a matching actual platform;
otherwise observations are NotValidated. Sparse, absent or mismatched-unit
metrics remain Unavailable.

The 0 B/frame target for steady lists/high-frequency interaction is **not achieved
by diagnostic refresh** and is not inferred from stage9/10 local synchronous GC
tests. Editor/test/diagnostic allocations can exceed it; reports say Exceeded.
A latest-sample budget report is not a frame-time distribution, stable FPS, GPU
certification, Android/iOS device result or IL2CPP verification.

## Runnable panel

In an **empty temporary scene**, attach `UIDiagnosticsSample` to a GameObject and
enter Play. Do not attach this independent composition beside the existing
LoadScene/GameBootstrap root. The original login startup and all eleven Config
registrations remain unchanged. The sample uses explicit minimal code assets,
not a second production resource path.

The sample page opens/reuses/pools a widget and can inject/clear real duplicate
subscriptions, an ended-owner asset lease/input lock and noncooperative scope
work. Close the panel to access page actions; Inject reopens it. Clear releases
actual tokens/leases and completes/drains the deliberately pending task.

The panel provides Refresh, page filtering, Next page details, target selection,
scrollable metadata and Back/Close. It borrows the same model/UI/recorder, uses
safe-content layout and existing Debug/modal/focus/input policies, and never
creates another EventSystem or private input gate. Refresh is manual; showing
or hiding leaves no subscriptions/locks. Text is bounded below uGUI's single-mesh
vertex limit (14,000 characters, 16 recent shortened events); use the immutable
snapshot/event APIs for full details. Adapters only flush on explicit refresh.

Sampler creation and panel display reject non-Editor/non-Development builds.
Core diagnostics can still be explicitly injected in release code; production
composition should keep it null unless intentionally opting in.
See [Testing.md](Testing.md) for the exact acceptance evidence and limitations.
