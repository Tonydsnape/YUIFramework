# Y2 Runtime Contracts

## Editor authoring and release policy

Stage 14's [Editor tooling](EditorTooling.md) reuses the existing Config batches,
resource addresses and presentation core. Generated controls are serialized
required references; hand partials and designer rows remain owned by their authors.
`UIRequiredReferenceAttribute` is metadata, not runtime reflection/scanning.
Editor previews own only transient clones/settings scopes, never application
services or source font/material/atlas assets.

Player builds now require an explicit MinimalDemo or Production `UIBuildProfile`.
The real preprocess hook and batch commands share one scoped validator.
Production uses `UIBuildGate.BuildPlayer(options, profile)` to audit actual scenes;
it never silently exempts built-in Resources or guesses unregistered business
loads/navigation. External demo-exclusion workspace preparation is explicitly
not validation. Official YooAsset bundle reports/version history are local output,
not certification of a device, backend deployment or runtime performance budget.

## Service ownership

Y2 code owns an `IUIService` instance and injects it at the application composition root:

```csharp
IUIService ui = new UIManager();
var rootRuntime = UIRootRuntime.CreateOwned();
await ui.InitializeAsync(resourceLoader, rootRuntime, cancellationToken: cancellationToken);
```

`UIManager.Instance` and `UIManager.Init` remain available only as a temporary Y1 compatibility entry. They use the same `UIManager` implementation; there is no second registry, navigator, pool, or message bus.

The owner must call:

```csharp
await ui.ShutdownAsync();
```

Shutdown stops both the per-type operation coordinator and the navigator's FIFO queue from accepting new commands, cancels the service lifetime so in-flight and still-queued operations observe cancellation, drains every lane (queued and running) before touching any state, closes active contexts, destroys pooled contexts, clears navigation/messages/registration, then disposes the root runtime. The injected resource loader is caller-owned; Y2 does not dispose it. External root/EventSystem objects remain caller-owned. See [Navigation.md](Navigation.md) and [UIRootAndInput.md](UIRootAndInput.md).

## Initialization rules

Stage 12 adds instance-owned `IUIService.Scopes` and `InputArbiter`.
`UIScope` reuses `UIPoolScope` identity but owns tasks and active/hidden UI as
well as idle entries. Real scene changes/unloads start teardown; await scope
release for a completed boundary. Manager shutdown cancels/drains scope work
before stopping the existing lanes/root. Loading, bounded Toast, FIFO typed
confirmation and global application notice policies borrow the same runtime.
They never dispose injected resources/Config/presentation/localization.
See [SystemUI.md](SystemUI.md) for cancellation, input and ownership contracts.

The optional [text localization](Localization.md) service borrows the instance Config
owner, reads its immutable catalog and never owns table leases or a second config store.
`ConfigLocalizedText.Bind(service, context)` follows the actual display token;
disable detaches observers, resume refreshes, and closed/pool contexts cannot retain
active display subscriptions. Strict `Get/TryGet` distinguish fallback from exact success.
Dispose localization after UI cleanup and before Config/resources.

The optional [presentation service](Presentation.md) borrows Config/text/resources
and the existing transition runner. Typed theme tokens read the published snapshot;
asset bindings own per-display font/sprite/material leases and fence stale results.
Scaling restores a captured baseline. Safe-area bindings inset content only,
never layer roots or modal blockers. Teardown adds presentation Dispose between UI
and text cleanup; external services remain alive.

- `Initialize` and `InitializeAsync` may only be called while uninitialized.
- Repeated initialization throws `InvalidOperationException`.
- A canceled `InitializeAsync` throws before changing service state.
- `ShutdownAsync` is idempotent while uninitialized.
- Cancellation is checked before shutdown starts. Once cleanup starts, shutdown completes cleanup without partial cancellation.
- The same service instance may be initialized again after a successful shutdown.
- Initialization accepts an explicit `UIRootRuntime`; the compatibility overload creates or binds one through the same implementation.
- Public service operations, including generic `CloseAsync<T>`, reject use before initialization and after shutdown with `InvalidOperationException`.

## Async baseline

Stage 13 adds optional borrowed `UIDiagnostics` to UIManager and UIResourceService,
not a second registry or singleton. Correlated immutable execution/ownership
snapshots, bounded events, explicit adapter flush/quarantine, actual profiler
sampling and target-specific budgets are defined in [Diagnostics.md](Diagnostics.md).
Defaults remain uninstrumented; the Dev panel/sampler are gated. No observer
owns UI resources, Config, localization or application cancellation.

Runtime, navigation, resource, and transition asynchronous contracts use:

- `UniTask` / `UniTask<T>`
- an optional `CancellationToken`
- cancellation propagation through `OperationCanceledException`

Each context operation links the caller, context lifetime, and service lifetime cancellation tokens. A canceled new open releases its instance and resource; a canceled pooled open returns the context to the pool; a canceled existing open restores its prior stable state; and a canceled close keeps the context reachable and restores transition visuals.

Lifecycle callback failures record `LastFailure` and surface as metadata-rich `UILifecycleException`. Rollback failures force release rather than retaining a damaged active or pooled context. See [Lifecycle.md](Lifecycle.md) for the complete state graph and callback phases.

## Registry

Stage 15 makes the existing reflection contract explicit:
`UIConfigRegistration.ValidateContextType`, `For<T>` and `Register<T>` require a
concrete closed Context class with a public parameterless constructor, before
publishing registration. Both generic Open and Type-based prewarm still construct
through Activator; this is not a factory API. Preserve/link.xml and concrete AOT
witnesses do not prove unknown generic IL2CPP combinations.
The optional borrowed CodeLoading extension reuses Bootstrap readiness and never
introduces a Runtime/Bootstrap dependency on Hotfix business or HybridCLR.
See [HybridCLRBoundary.md](HybridCLRBoundary.md).

The independent [Config integration](Config.md) adds `ConfigService` atomic typed
snapshots and `UIManager.RegisterBatch(IReadOnlyList<UIConfigRegistration>)`.
`UIConfigRegistration.For<T>` validates and copies input; batches never replace existing
types/identities, and invalid batches insert nothing. `ConfigUIStartup` loads before
mapping/registering and enters business code only after success. Config and resources
remain separately owned; shutdown order is UI, Config, resources, Bootstrap.

`IUIRegistry` provides:

- `Register<T>(UIConfig)`
- `IsRegistered<T>()`
- `TryGetConfig(Type, out UIConfig)`

`UIConfig.Id` is exposed as an ordinal, case-sensitive `UIKey`. Empty keys are rejected.

Stage 4 adds validated `UILayerProfile`/`UILayerDescriptor` runtime descriptors. The mutable `UIConfig` registration adapter remains during the migration window.

## Handles

`OpenHandleAsync<T>` returns `UIHandle<T>`:

- `Key` identifies the registered UI.
- `Context` exposes the current context during the migration window.
- `IsOpen` checks that the handle still refers to the active context.
- `CloseAsync` routes closing through the service that created the handle.
- Closing a stale handle is a no-op and cannot remove a newer context of the same type.

Reference identity prevents a stale handle from closing a newer active context of the same type.

For YooAsset loads, cancellation stops the caller's wait immediately. If YooAsset cannot abort the underlying provider operation, the framework retains a background release continuation and releases the handle as soon as that operation completes.

## Context dependency scope

`BaseContext` receives its owning `IUIService` when bound. Protected message helpers now use the owning service's `IUIMessageBus`; they no longer route through `UIManager.Instance`.

Derived contexts can access the owning service through the protected `Services` property. This is the supported route for navigation and service interaction in injected Y2 applications.

Stage 9 adds typed `UIMessageTopic<T>` helpers for lifetime and display subscriptions. Topic
payload mismatch is an immediate error, subscriber failures aggregate after stable ordered
dispatch, and `UIMessageScope` binds explicit subscriptions to cancellation/disposal. See
[Messaging.md](Messaging.md).

## Navigation

`IUINavigator` exposes the current navigation API using UniTask and cancellation.

Stage 3 gives every UI operation FIFO ordering per context type, gives the navigator its
own single FIFO transaction queue (`IsBusy` reports its activity), adds a public
`BringToTopAsync<T>`, a `NavigateBackAsync` alias for `BackAsync`, an async navigation
guard extension point, and non-destructive-first transaction rollback with
destructive-failure convergence for Push/Pop/Replace/BringToTop. Per-context lifecycle
re-entry still fails immediately with `UIOperationInProgressException` as a last-resort
safety net; the callback/guard-scoped reentrancy check
(`UIOperationReentrancyException`) is now the first line of defense and fires before a
callback could ever deadlock by awaiting its own in-progress command. See
[Navigation.md](Navigation.md) for the full merge policy, ordering guarantees, guard
contract, and documented limitations.

## Root, interaction, and input

`IUIService.RootRuntime` exposes the explicit uGUI composition boundary and
`IUIService.InputLocks` exposes reference-counted input leases. Ten canonical layers,
bounded sorting leases, one modal mask/stack, focus restoration, and Escape/Android Back
routing are defined in [UIRootAndInput.md](UIRootAndInput.md).

## Resource ownership

`IUIResourceService` is the stage 5 resource contract. It exposes strong `UIResourceKey`
(package + location + type) keys, asset leases (`IUIAssetLease<T>`) and instance leases
(`IUIInstanceLease`) as separate, independently owned, idempotently released units.
Concurrent loads of one key share a single native load while every caller receives its own
lease; cancelling one caller cancels only that caller's wait. Providers are registered per
package through `IUIResourcePackageRegistry`, with an explicit default UI package. YooAsset
3.x is the only production backend and Addressables has been removed. See
[Resources.md](Resources.md) for the full ownership rules, cancellation semantics, preload
and batch behavior, and leak diagnostics.

`UIManager.Initialize(IUIResourceService)` opts a service instance into this model, and
`UIManager.ResourceService` exposes it. `UIManager.Initialize(IResourceLoader)` remains the
unchanged Y1 compatibility path.

The package registry freezes when its owning resource service first loads or starts shutdown.
`UIResourceService.ShutdownAsync` is one shared cleanup task; native/provider failures are
aggregated, and a native release is counted only after it succeeds.

## Compatibility window

The following remain Y1 compatibility APIs:

- `UIManager.Instance`
- `UIManager.Init`
- object-based open arguments
- direct `BaseContext` exposure
- mutable `UIConfig`
- string-based messages

New code should start from `Y2ServiceBootstrap` and use an owned `IUIService`.

## Resource bootstrap

Stage 6 introduces the instance-owned `BootstrapRunner` and immutable `BootstrapProfile`.
The runner owns one centralized state graph from `Idle` through package/version/manifest,
download and verification to `ResourcesReady`, optional `LoadingCodeExtension`,
`EnteringGame`, and `Completed`. `Failed`, `Canceled`, `Resetting`, and `ShuttingDown` are
explicit outcomes/states.

`IBootstrapBackend` is independent of YooAsset. Network, clock/delay, disk, confirmation,
progress, telemetry, code loading, and game entry are injected contracts. Equal concurrent
profiles share one run; a different profile is rejected; caller cancellation cancels only
that wait. Reset and shutdown cancel and drain the underlying run.

`BootstrapRunResult.IsSuccess` requires `Completed` plus a non-null verified
`BootstrapReadyContext`. Business entry is impossible before resource verification and code
loader success. A verified local/built-in fallback is marked degraded and never represented
as the latest remote version. See [Bootstrap.md](Bootstrap.md).

## Pooling and memory governance

Stage 7 distinguishes resource preload from initialized-instance prewarm. The former leaves
an unreferenced native asset cache; `UIManager.PrewarmAsync<T>` consumes
`UIConfig.PreloadCount` and creates distinct hidden contexts that retain explicit instance
leases. Prewarm/Open/Close share the same per-type FIFO lane.

Pool entries are claimed by one pool and carry type/key, scope, priority, monotonic LRU age,
and lease-source identity. Duplicate, cross-pool, mismatched, and destroyed returns are
rejected and diagnosed. Per-key and global limits, idle expiry, priority/LRU eviction,
scene/module/global scopes, low-memory cleanup, and read-only snapshots are defined in
[Pooling.md](Pooling.md).

`LifetimeToken` remains permanent for the initialized context. A separate `DisplayToken`
owns framework-tracked display tasks, input locks, subscriptions, and temporary bindings;
pooling cancels only that display scope and preserves permanent initialization state.

## Transitions and visibility

Stage 8 adds `IUIService.Transitions`, `RequestTransitionInterruption`, and
`RefreshTransitionBaseline`. Built-in transitions use one generation-fenced writer per
view and preserve captured alpha, scale, and anchored-position baselines. Default operation
ordering remains FIFO; interrupt/reverse/skip affect only the active visual await and leave
lifecycle rollback/commit on the existing lane.

`BaseContext.VisibilityState` exposes orthogonal `Visible`, `Interactable`, `Covered`, and
`Suspended` flags. `UIInteractionController` remains the sole raycast authority.
`WaitUntilResumedAsync` gates framework-managed display work and is linked to display and
lifetime cancellation. See [Transitions.md](Transitions.md).

## Messaging and MVVM lifecycle

`IUIMessageBus` exposes typed topics, priorities, stable subscription order, explicit scopes,
and main-thread-confined mutation. Obsolete string overloads forward to the same typed
channel engine.

`IReadOnlyObservableProperty<T>`, `IObservableProperty<T>`, validated properties,
`IUICommand`, `UICommand`, and `UIAsyncCommand` are public runtime contracts with no business
assembly or reflection dependency. `UIDataBinding` covers common uGUI and TextMeshPro
controls. `BaseContext.SetViewModel` distinguishes `Owned` from `External`; binding scope is
tracked independently from ViewModel ownership. See [MVVM.md](MVVM.md).

## Commercial virtual lists

Stage 10 separates stable-ID `UIListDataSource<T>`, `UIListSelection`, and generation-fenced
`UIListItemBinding` from the optional `SuperScrollViewList<T>` native List/Grid backend.
Collection mutations drive incremental view updates; item reuse always ends the prior bind.
The licensed plugin is installed locally into an excluded directory, never referenced by
the core Runtime assembly. Ownership, installation, supported layouts, and the obsolete
`UIVirtualList` forwarding contract are defined in [Virtualization.md](Virtualization.md).
