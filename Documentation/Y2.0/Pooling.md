# YUIFramework Y2.0 Pooling and Memory Governance (Stage 7)

Stage 7 turns the original per-type stack into an ownership-aware idle-instance cache. It
does not change the stage 2 lifecycle graph, stage 3 FIFO navigation transactions, stage 4
root/input rules, stage 5 resource lease accounting, or stage 6 bootstrap shutdown order.

## Resource preload versus instance prewarm

These operations intentionally have different ownership:

- `IUIResourceService.PreloadAsync` loads native assets, releases the temporary asset leases,
  and leaves unreferenced resource-cache entries. It creates no `GameObject` or `BaseContext`.
- `UIManager.PrewarmAsync<T>` consumes `UIConfig.PreloadCount`, creates distinct initialized
  context/view instances, and puts them into the idle instance pool. Every prewarmed instance
  retains its own instance lease until it is evicted or released.

`PreloadCount` requires `CacheOnClose=true` and a positive `MaxPoolSize`; the effective target
is `min(PreloadCount, MaxPoolSize)`. Prewarm is finite, cancellable, and serialized on the
same context-type lane as Open/Close. It never calls `OnShow`, never activates the view, and
never adds navigation, active-registry, sorting, modal, focus, or input state.

```csharp
ui.Register<InventoryPage>(new UIConfig
{
    Id = nameof(InventoryPage),
    PrefabKey = "UI/Inventory",
    CacheOnClose = true,
    MaxPoolSize = 3,
    PreloadCount = 3,
    PoolPriority = 10,
    PoolIdleTimeoutSeconds = 120
});

await ui.PrewarmAsync<InventoryPage>(cancellationToken);
// Or consume every registered configuration:
await ui.PrewarmRegisteredAsync(cancellationToken: cancellationToken);
```

Successful prewarm retains all instances created by that call. Failure or cancellation
removes and fully releases every instance created by that call. Instances that existed
before the call are not rolled back.

## Entry ownership and return rejection

Each `UIPooledObject` has an immutable entry identity and records context type, `UIKey`,
prefab key, scope, priority, monotonic idle timestamp, and lease source. The first accepting
pool claims the entry. A return is rejected with `UIPoolReturnRejection` when it is null or
destroyed, has a mismatched type, is already idle in that pool, belongs to another pool, or
cannot fit the configured capacity.

Externally destroyed idle entries are diagnosed and passed back to `UIManager` for complete
context finalization. `OnDestroy`, lifetime cancellation, runtime cleanup, and instance lease
release still run; the entry is never silently dropped.

## Capacity, LRU, priority, and expiry

`MaxPoolSize` remains the per-key/type capacity and `UIObjectPool(globalCapacity, clock)` adds
a finite global capacity. When an advanced return needs space, the lowest priority entry is
selected first; ties use the oldest injected monotonic timestamp, then immutable entry ID.
Only idle pool entries participate. Active or borrowed contexts are never capacity, expiry,
scope, or low-memory victims.

`PoolIdleTimeoutSeconds` opts a key into expiry. `EvictExpiredPoolEntries` performs an
explicit sweep; normal pool acquisition also removes expired entries before selecting a hit.
`IUIPoolClock` makes ordering deterministic in tests and avoids wall-clock jumps.

## Scope identity and release

Three scope kinds exist:

- `UIPoolScope.Global` is process/service-wide and cannot be explicitly ended.
- `GetSceneScope(Scene)` uses the real Unity scene handle. `SceneManager.sceneUnloaded` is
  subscribed during manager initialization and releases that scene's idle entries.
- `CreateModuleScope(name)` creates an explicit owner identity; the owner must call
  `ReleaseScopeAsync`.

Use `OpenInScopeAsync<T>` and the scoped `PrewarmAsync<T>` overload. Ending a scope first
cancels its token, then drains every affected type lane and releases its idle entries. An
in-flight prewarm/open rechecks the scope before publication, so it cannot resurrect the
scope. Active UI is not treated as idle and is not destroyed by scope release; when it later
closes, the ended scope forces destructive release instead of recaching it. This is pooling
ownership only, not the stage 12 scene/system-UI service.

## Display scope

`LifetimeToken` still spans `OnInit` through terminal `OnDestroy`. Every activation from new,
hidden, or pooled state has a separate `DisplayToken` and `DisplayArguments`. Before
`OnShow`, the context clears the previous display scope, calls
`HandleResetForReuse(previousArgs, nextArgs)`, and creates a new display token. Refreshing an
already-open context retains its current display scope so a failed refresh can still roll
back to the original stable visible state. A successful hide/close, pooled rollback,
terminal release, or shutdown cancels and clears the display scope.

Framework-owned transient work can be registered through:

- `RunDisplayTask`
- `SubscribeDisplayMessage`
- `TrackDisplayBinding` / `TrackDisplayResource`
- `AcquireDisplayInputLock`

Permanent subscriptions and bindings created with the existing lifetime helpers remain
alive while pooled and are released only by `OnDestroy`. Stage 7 does not redesign MVVM.
Cancellation of a hide transition occurs before display cleanup, so close cancellation
returns to the stable visible state with its display scope intact.

## Low memory and shutdown

`UIManager` subscribes to `Application.lowMemory` while initialized. A low-memory pass:

1. releases every idle instance in priority/LRU order,
2. continues after individual cleanup failures,
3. trims only unreferenced resource-cache entries, and
4. reports collected failures as one `AggregateException`.

Active contexts and their resource leases remain owned. Shutdown unhooks low-memory and
scene events before draining navigation and per-type lanes, then releases UI and pool state.
It does not shut down a caller-owned `IUIResourceService`; bootstrap ownership remains
UI -> resource composition -> backend.

## Diagnostics

`UIManager.PoolDiagnostics` is a read-only snapshot with hit/miss, eviction, prewarm,
rejected-return, invalid-entry, idle, idle-with-lease, per-entry scope/priority/age, and
global-capacity data. `EstimatedInstanceCount` is explicitly an object count, not a claim
about native or managed bytes. Resource/native lease counts remain in
`IUIResourceService.GetDiagnostics()`.

## Stage boundary

Stage 7 provides scopes only as pool ownership identities. It does not implement stage 8
interruptible transitions, stage 9 typed messaging/MVVM redesign, or stage 12 scene/system UI
services.

Stage 8 integrates with this ownership model without changing it: a completed hide restores
the captured visual baseline immediately after deactivation and before an entry becomes
idle. A pooled rebind can refresh that baseline at a stable inactive point. Transition
generation is forgotten on terminal release, and prewarm never starts a visual transition.
