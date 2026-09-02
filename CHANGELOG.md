# Changelog

All notable changes to YUIFramework are documented in this file.

## [Unreleased] - Y2.0

### Added

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

### Changed

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

### Migration

- Y2.0 permits breaking API changes behind a temporary Y1 compatibility facade.
- YooAsset 3.x will become the only production resource backend.
- Runtime asynchronous APIs will migrate to UniTask with `CancellationToken`.
- Stage 4 is complete. Stage 5 YooAsset resource leases and shared-load ownership have not
  started.
