# Changelog

All notable changes to YUIFramework are documented in this file.

## [Unreleased] - Y2.0

### Added

- Independent post-stage-10 Config integration: validated Excel base/normal protocol,
  JSON/MessagePack and typed C# generation with multi-directory rollback, instance-scoped
  atomic ConfigService snapshots, cancellation/reload/lease ownership and explicit UI batches.
- Migrated all six existing example UI registrations to the UISettings workbook and
  typed profile mappings; preserved manual Register compatibility and stage 11 boundary.
- Config protocol/transaction, real generated-table, Bootstrap composition and
  UI/resource regression gates; pinned Newtonsoft/MessagePack dependencies.

- Y2.0 baseline documentation and Y1-to-Y2 API migration matrix.
- EditMode characterization tests for pooling, messaging, observable properties, and transition configuration.
- PlayMode characterization tests for registration, lifecycle, caching, and stack navigation.
- Injectable `IUIService`, `IUIRegistry`, `IUINavigator`, and `IUIMessageBus` contracts.
- Strong `UIKey` and owning-service `UIHandle<T>` primitives.
- Explicit initialize/shutdown lifecycle and injected-service Y2 sample.
- Deterministic context lifecycle state machine with operation IDs, failure metadata, rollback, and close disposition.
- Stage 2 lifecycle contract and state-graph documentation.
- `UIOperationCoordinator`: a per-context-type FIFO command queue with a conservative
  single-flight merge policy for equivalent concurrent first-creation `Open` calls,
  callback/guard-scoped reentrancy detection, and safe shutdown quiescing.
- `UINavigator` FIFO transaction queue covering Push/Pop/Replace/Back/BringToTop, public
  `IsBusy`, public `BringToTopAsync<T>`, and a `NavigateBackAsync` alias for `BackAsync`.
- Async navigation guard extension point (`IUINavigator.Guard`, `UINavigationGuard`,
  `UINavigationRequest`) and `UINavigationRejectedException`.
- `UIOperationReentrancyException` for lifecycle-callback/guard reentrancy that would
  otherwise deadlock a FIFO queue by awaiting itself.
- Stage 3 navigation/concurrency documentation (`Documentation/Y2.0/Navigation.md`).
- PlayMode characterization tests for coordinator FIFO ordering, single-flight merge and
  its cancellation semantics, shutdown quiescing, navigator transaction rollback and
  destructive-failure convergence, and navigation guards
  (`UIOperationCoordinatorCharacterizationTests`, `UINavigatorTransactionCharacterizationTests`).
- Explicit owned/external `UIRootRuntime`, deterministic root/EventSystem validation, and
  lifecycle-safe static reset.
- Validated ten-layer profiles, compact bounded sorting leases, modal stack/shared mask,
  centralized raycast eligibility, reference-counted input locks, focus restoration, and
  Escape/Android Back routing.
- Stage 4 contract and acceptance documentation (`Documentation/Y2.0/UIRootAndInput.md`).
- Stage 5 resource ownership: strong `UIResourceKey` (package + location + type), asset leases
  (`IUIAssetLease<T>`) and instance leases (`IUIInstanceLease`) as separate, idempotently
  released units of ownership.
- `IUIResourceService`/`UIResourceService` with single-flight shared loading, reference
  counting, per-waiter cancellation, an unreferenced cache, preload, batch loading with
  partial-failure aggregation, low-memory trimming, leak diagnostics and draining shutdown.
- `IUIResourceProvider`, `IUINativeAssetHandle`, and `IUIResourcePackageRegistry`/
  `UIResourcePackageRegistry` with an explicit default UI package, plus
  `UIResourcePackageNotFoundException` and `UIResourcePackageAlreadyRegisteredException`.
- `YooAssetResourceProvider` (production backend, injected with a `ResourcePackage` instead of
  depending on the `HotUpdateManager` singleton) and `ResourcesResourceProvider`
  (tests/Editor compatibility only, obeying the same lease rules).
- `UIManager.Initialize(IUIResourceService)` and `UIManager.ResourceService`: opened contexts
  now hold an explicit instance lease.
- Stage 5 documentation (`Documentation/Y2.0/Resources.md`) and EditMode/PlayMode resource
  ownership suites driven by a fake provider that needs neither YooAsset nor a network.
- Stage 6 immutable `BootstrapProfile`, centralized `BootstrapStateGraph`, correlated
  `BootstrapRunResult`/failure/progress/telemetry models, and injected backend, clock/delay,
  network, disk, confirmation, code-loader, and game-entry contracts.
- `BootstrapRunner` with deterministic EditorSimulate/Offline/Host paths, primary/fallback
  CDN retry and bounded exponential backoff, timeout classification, download confirmation,
  disk checks, verification gates, verified degraded fallback, per-caller cancellation,
  equal-profile single-flight, reset, shutdown, and consecutive-run support.
- `YUIFramework.Bootstrap.YooAsset` with the YooAsset 3.0.5 production backend,
  application/channel/version/platform/package-scoped URL resolution, multi-package ready
  contexts, and `YooAssetBootstrapComposition`.
- Default no-op `IBootstrapCodeLoader`, leaving code hot update/HybridCLR unbound while
  guaranteeing that code loading runs after resources and before business entry.
- Stage 6 fake-backend and adapter EditMode coverage plus
  `Documentation/Y2.0/Bootstrap.md` and `Documentation/Y2.0/Migration.md`.
- Stage 7 ownership-aware instance pooling with real `PreloadCount` prewarm, per-key/global
  capacity, priority/LRU and idle expiry, scene/module/global scopes, low-memory trimming,
  display-scope cleanup, read-only diagnostics, and `Documentation/Y2.0/Pooling.md`.
- Stage 8 generation-fenced fade/scale/slide sessions, explicit interrupt/reverse/skip
  signals, immutable curve/config snapshots, custom `IUITransition` registration,
  stable visual baselines, unscaled/injectable timing, and
  `Documentation/Y2.0/Transitions.md`.
- Orthogonal context visibility (`Visible`, `Interactable`, `Covered`, `Suspended`) and a
  display/lifetime-bound cooperative suspension gate, with navigation/modal/input
  composition and rollback restoration.
- Built-in transition teardown now retires registry identity before cancellation, preventing
  delayed continuations from writing rebound views; disposal continues canceling remaining
  sessions when an individual callback fails and reports the failures together.
- Stage 9 typed message topics with stable priority ordering, scoped subscriptions,
  dispatch-error aggregation, and allocation-free steady-state typed publish.
- Awaitable sync/async commands, validation/read-only observable contracts, owned/external
  ViewModel policy, and code-first uGUI/TextMeshPro bindings for common controls.
- Stage 10 optional SuperScrollView 2.5.3 integration: locally installed licensed runtime,
  original List/Grid adapter, stable-ID selection, collection mutations and anchor
  preservation, dynamic List sizing, generation-fenced sprite leases, and Context-owned
  display/suspension cleanup. No commercial supplier sources are included for redistribution.
- Native-aware nested drag routing with paired cancellation and touch-safe layout rebasing,
  10,000-row examples/tests, and bounded real-scroll allocation measurements.

### Changed

- `UIVirtualList` is an obsolete forwarding facade over the installed commercial adapter;
  its separate kernel is removed. Missing installation and unsupported legacy layout
  options fail explicitly. See `Documentation/Y2.0/Virtualization.md`.

- Documentation now identifies Unity `2022.3.62f2` as the verified project baseline.
- Runtime, navigation, resource, and transition asynchronous APIs now use UniTask and `CancellationToken`.
- `BaseContext` message helpers now use the context's owning service instead of the global singleton.
- Repeated initialization is rejected until `ShutdownAsync` completes.
- Canceled opens roll back new or pooled instances; canceled closes keep contexts reachable and restore transition visuals.
- YooAsset waits now observe cancellation without leaking the still-running native resource handle.
- Stale UI handles can no longer close a newer context of the same type.
- Lifecycle operations now link caller, context, and service cancellation.
- Pool clearing and shutdown aggregate cleanup failures after attempting every release.
- Generic `CloseAsync<T>` now consistently rejects calls before initialization and after shutdown.
- **Behavior change (stage 3):** public same-context-type `Open`/`Close`/`Hide`/`Show`
  calls now queue FIFO instead of failing fast with `UIOperationInProgressException`. The
  stage 2 characterization test for the old fail-fast behavior
  (`OperationInProgress_FailsFastUntilCurrentOperationFinishes`) was replaced by
  deterministic FIFO/merge tests; the underlying `UIOperationInProgressException` guard on
  `BaseContext` is unchanged and remains a last-resort safety net.
- **Behavior change (stage 3):** pushing or replacing onto a page type that already exists
  elsewhere in the navigation stack always brings it to the top instead of ever creating a
  duplicate stack entry. `UINavigateOptions.BringExistingPageToTop` remains for source
  compatibility but no longer changes this.
- Navigator transactions now evaluate guards and snapshot the stack at execution time, use
  non-destructive-first ordering (show/open before a destructive hide/close) with rollback
  on failure, and converge to a minimal consistent stack instead of ever fabricating or
  reopening an identity that was already destructively released.
- `HelloUIBootstrap` now routes Escape through `NavigateBackAsync` and no longer needs a
  manual boolean re-entrancy lock; the navigator's own FIFO queue serializes repeated
  presses.
- `UIManager` now accepts or creates a root runtime and disposes it only after stage 3
  navigation/per-type lanes, active contexts, and pools are drained.
- Concurrent shutdown calls share one cleanup operation; input composition covers
  descendant raycasters and keyboard focus, and Escape cannot also dispatch uGUI Cancel.
- Hidden contexts retain sorting identity; pooled/released/faulted contexts release it.
- Production examples no longer depend on `UIRoot.Instance` or `async void` input loops.
- **Stage 5:** `UIManager` now reclaims orphaned instance leases on the pooled-open path.
  `IUIObjectPool.TryGet` silently drops pooled entries whose view object was destroyed outside
  the framework, so those entries never reached `ReleaseContextInternal`; their resource
  reference count would otherwise never reach zero and `TrimUnused`/`HandleLowMemory` could
  never reclaim the asset.
- **Stage 5 (documented limitation):** because the stage 3 operation coordinator reports
  cancellation before the queued operation body unwinds, an open cancelled after the instance
  lease was registered (possible once a show transition is running) returns
  `OperationCanceledException` while the lease is still held for one or more frames. The lease
  is still returned deterministically when the body unwinds and is never double-released, but
  the exception is not a synchronization point for resource release.
- Resource bootstrap is now instance-owned and named independently from code hot update.
  `YUIFramework.HotUpdate` contains only `[Obsolete]` forwarding facades; the old MonoScript
  `.cs.meta` GUIDs remain intact, all legacy callbacks reset on subsystem registration, and
  failures no longer become success-shaped built-in-resource results.
- `YooAssetResourceProvider` moved from `YUIFramework.HotUpdate` to
  `YUIFramework.Bootstrap.YooAsset` with its original `.cs.meta` GUID preserved.
- `GameLauncher`, the resource-startup sample, editor profile tool, progress UI, and
  `GaneBootstrap` now use explicit cancellation/error observation and contain no `async void`.
- Review hardening validates package/version filename segments before YooAsset path use,
  enables high verification for built-in/sandbox caches, reserves aggregate disk bytes per
  storage scope, tracks/drains canceled YooAsset operations, marshals downloader cancellation
  through the PlayerLoop, and reference-counts global YooAsset ownership across backends.
- Cancellation is rechecked after code loading and game entry; Reset joins active Shutdown and
  conditional state transitions are atomic.
- Stage 5 resource registries freeze on first use, and resource shutdown is one shared task
  that aggregates provider/native cleanup failures.
- Final review fencing prevents late timed-out YooAsset operations from overwriting newer
  manifests, accepted legacy runs are atomic with lifecycle shutdown, backend shutdown still
  runs after reset failure, and partially destroyed package sets remain retryable.
- Failed zero-reference releases remain tracked until shutdown, canceled loads publish their
  shared completion only after release finalization, invalid pooled contexts are removed and
  finalized across context types, and compatibility URL paths use the hardened segment parser.
- Closeout hardening fences local fallback behind a zero-download proof, attempts every package
  during teardown, generation-gates downloader progress, retries faulted legacy shutdown,
  and makes `GameLauncher` shut down only the UIManager instance it initialized while still
  attempting composition/backend cleanup after any earlier cleanup failure.
- Repeated `GameLauncher.LaunchAsync` calls are single-flight, readiness is published only after
  listeners succeed, the startup sample attempts every teardown stage, and a faulted
  `UIResourceService` shutdown can retry retained cleanup work.
- Pool entries now reject duplicate, cross-pool, mismatched, or destroyed returns; externally
  destroyed idle instances are fully finalized instead of silently discarded.
- Resource preload and initialized-instance prewarm are separate operations. Prewarm shares
  the per-type FIFO lane with Open/Close, rolls back call-local partial work, and cannot
  repopulate an ended scope.
- Context display tokens, tracked tasks/input locks/messages/bindings, and transient arguments
  are reset on pooling while permanent Init-time lifetime state remains intact.
- Show and navigation-hide animations now run inside their existing per-type FIFO lifecycle
  operations. Built-in visual writes are single-owner and generation-fenced; pooled reuse
  restores captured non-default alpha, scale, and position without drift.
- String message APIs are obsolete facades over the typed engine. Context lifetime/display
  helpers, commands, bindings, and ViewModels now share deterministic cleanup and aggregate
  failures after attempting every item.

### Migration

- Y2.0 permits breaking API changes behind a temporary Y1 compatibility facade.
- YooAsset 3.x is the only production resource backend as of stage 5; Addressables support
  has been removed, and Resources is limited to tests and minimal Editor compatibility.
- Runtime asynchronous APIs use UniTask with `CancellationToken`.
- Stage 9 messaging and MVVM lifecycle is complete. Stage 10 virtual-list work has not
  started.
