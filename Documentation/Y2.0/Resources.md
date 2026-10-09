# YUIFramework Y2.0 Resource Ownership (Stage 5)

Stage 5 replaces the ad-hoc `IResourceLoader` ownership rules with an explicit, testable
resource ownership model. YooAsset 3.x is the only production backend.

## Decisions

- **YooAsset 3.x is the only production resource backend.**
- **Addressables support is removed.** `AddressablesLoader`, the `YUIFRAMEWORK_ADDRESSABLES`
  version define, and the related documentation no longer exist.
- **Resources remains only for tests and minimal Editor compatibility**, and it obeys exactly
  the same lease and reference-counting rules as YooAsset.
- The ownership core lives in `YUIFramework.Runtime` and is backend agnostic. The YooAsset
  adapter lives in `YUIFramework.Bootstrap.YooAsset`, downstream of the pure
  `YUIFramework.Bootstrap` state machine. This keeps ownership and bootstrap tests independent
  of YooAsset and the network.

## Types

| Type | Assembly | Responsibility |
|---|---|---|
| `UIResourceKey` | Runtime | Strong key: `package` + `location` + `assetType` |
| `IUIAssetLease` / `IUIAssetLease<T>` | Runtime | One share of ownership of a loaded asset |
| `IUIInstanceLease` | Runtime | Ownership of one instantiated `GameObject` |
| `IUINativeAssetHandle` | Runtime | Abstraction over a backend handle (YooAsset `AssetHandle`) |
| `IUIResourceProvider` | Runtime | One backend per package |
| `IUIResourcePackageRegistry` / `UIResourcePackageRegistry` | Runtime | Package name → provider, explicit default package |
| `IUIResourceService` / `UIResourceService` | Runtime | Shared loading, ref counting, leases, preload, trim, diagnostics |
| `UIResourceDiagnosticsSnapshot` | Runtime | Leak/ref-count snapshot |
| `UIResourceBatchResult` | Runtime | Batch load with partial-failure aggregation |
| `ResourcesResourceProvider` | Runtime | Resources backend, tests/Editor only |
| `YooAssetResourceProvider` | Bootstrap.YooAsset | Production YooAsset backend |

## Ownership rules

1. **Single-flight.** Concurrent loads of the same key trigger exactly one native load.
   Every caller receives its own independent lease over the same underlying asset.
2. **Independent leases.** Releasing one lease never affects another holder.
3. **Per-waiter cancellation.** Cancelling one caller cancels only that caller's wait.
   The shared native load and all other waiters are unaffected.
4. **Abandoned loads are released immediately.** The native load is not cancelable, so when
   *every* waiter has cancelled, the service releases the handle as soon as the load completes.
   This is why `IUIResourceProvider.LoadAssetAsync` deliberately takes no `CancellationToken`.
5. **Asset and instance ownership are separate.** `LoadAssetAsync` yields an asset lease.
   `InstantiateAsync` yields an instance lease that additionally owns a `GameObject`, and
   internally holds one asset lease. Releasing the instance destroys the instance and returns
   only its own asset lease.
6. **Release is idempotent.** `Release()` and `Dispose()` may be called any number of times;
   the reference count is returned exactly once, and a native handle is released at most once.
7. **Zero-reference cache.** When the last lease is released the entry is kept as an
   unreferenced cache entry so a reopen is cheap. It is reclaimed by `ReleaseUnused`,
   `TrimUnused`, `HandleLowMemory` or `ShutdownAsync`.
   Full LRU and capacity governance is stage 7 and is intentionally not implemented here.
8. **Keys never merge across package, location or type.** All three must match.
9. **Unknown or duplicate packages fail explicitly** with
   `UIResourcePackageNotFoundException` / `UIResourcePackageAlreadyRegisteredException`,
   and an unknown package never creates an entry.
10. **Provider identity freezes on first use.** Register/unregister fails after the first load
    or shutdown begins, so a cached entry can never impersonate a replacement provider with
    the same package name.

### UniTask multi-await bridging

A `UniTask` may only be awaited once. The shared load is stored as
`UniTaskCompletionSource<LoadOutcome>.Task.Preserve()` so that many waiters can await it, and
each waiter attaches its own cancellation with `AttachExternalCancellation`.

The shared task **never faults**: failures travel inside `LoadOutcome.Error` and each waiter
throws its own `ResourceLoadException`. This is what guarantees that an abandoned, failed load
can never raise `UnobservedTaskException`.

## Usage

```csharp
var composition = YooAssetBootstrapComposition.Create(bootstrapResult.ReadyContext);
var resources = composition.ResourceService;
UIManager.Instance.Initialize(resources);
```

Loading and instantiating:

```csharp
using var lease = await resources.LoadAssetAsync<Sprite>("UI/Icons/Coin", cancellationToken: token);
var sprite = lease.Asset;

var instance = await resources.InstantiateAsync(
    UIResourceKey.Of<GameObject>("UI/Pages/MainMenuPage"),
    parent,
    token);
// ...
instance.Release(); // destroys the instance and returns its asset lease
```

Generic asset types are supported through `UIResourceKey.Of<T>()`: `GameObject`, `Sprite`,
`Texture2D`, `Material`, `Font`, `AudioClip`, and any other `UnityEngine.Object`.

### Multiple packages

```csharp
registry.Register(new YooAssetResourceProvider(uiPackage), isDefault: true);
registry.Register(new YooAssetResourceProvider(dlcPackage));

var dlcKey = UIResourceKey.Of<GameObject>("UI/Dlc/Banner", "DlcPackage");
```

The default package is explicit: the first registered package becomes the default unless a
later registration passes `isDefault: true`.

## Preload, batch and low memory

```csharp
await resources.PreloadAsync(new[] { keyA, keyB });   // becomes unreferenced cache
var batch = await resources.LoadBatchAsync(new[] { keyA, keyBad });
if (batch.HasFailures) { Debug.LogError(batch.ToAggregateException()); }
foreach (var lease in batch.Leases) { /* ownership is yours */ }
batch.Dispose();

resources.HandleLowMemory();  // only clears unreferenced entries
```

`LoadBatchAsync` never discards successful items because of a failed sibling. If the whole
batch is cancelled, the already-acquired leases are released rather than leaked.

## Diagnostics and leak reporting

```csharp
var snapshot = resources.GetDiagnostics();
Debug.Log(snapshot);                       // entry/lease/waiter/native counters
snapshot.HasOutstandingLeases;             // suspected leak
snapshot.LeasedEntries;                    // who still holds references
snapshot.UnreferencedEntries;              // reclaimable cache
snapshot.IsNativeBalanced;                 // native loads == native releases
```

## UIManager integration

`UIManager.Initialize(IUIResourceService)` opts into the stage 5 model. From then on:

- The first creation of a context acquires **one instance lease**, held by `UIManager`.
- Cancellation, initialization failure, show failure and load failure all release the lease,
  with no leak and no double release.
- A pooled close **retains** the instance lease, because the instance stays alive in the pool.
- Externally destroyed pooled entries are removed and fully finalized across all context types
  before another open; they cannot later fall through to a null legacy loader.
- `ClearPool`, pool overflow, destructive close and `ShutdownAsync` all release it, and the
  reference count returns to zero.

`UIManager.Initialize(IResourceLoader)` keeps the legacy Y1 behavior byte for byte, so
stage 0-4 code and tests are unaffected. `UIManager.ResourceService` is non-null only on the
stage 5 path.

Stage 7 instance prewarm is distinct from `IUIResourceService.PreloadAsync`: resource preload
creates no GameObjects and ends with zero leases, while each idle prewarmed UI instance owns
one `IUIInstanceLease` until pool eviction. See [Pooling.md](Pooling.md).

## Shutdown

`ShutdownAsync` rejects new loads, waits for in-flight native loads to finish (they cannot be
cancelled), releases every remaining handle exactly once, and then shuts down each provider.
All concurrent callers await the same cleanup task, and later callers reuse a successful
result. A faulted cleanup can be retried so retained handles/providers are not stranded.
Cleanup attempts every provider and aggregates native/provider failures instead of returning
a success-shaped result. It never holds the internal lock while awaiting, so it cannot
deadlock against an in-flight load.
Failed `ReleaseUnused`/`TrimUnused` calls throw and retain the unresolved idempotent handle for
the shutdown pass; native balance is not incremented until `Release()` actually succeeds.

Domain reload is handled by rebuilding the service: all state lives in instance fields, and
there is no static mutable resource state to survive a reload.

When resources come from stage 6 bootstrap, shut down in ownership order: UI services first,
then `YooAssetBootstrapComposition.ShutdownResourceServiceAsync()`, then
`BootstrapRunner.ShutdownAsync()`. The provider does not own its `ResourcePackage`; the
YooAsset bootstrap backend destroys packages only after every resource handle is released.

## Test coverage

Stage 10's optional SuperScrollView adapter leases the **prefab asset**, not each native
item instance: the supplier pool performs its own Instantiate/Destroy. The list retains that
lease through destruction of every item and the private template. Generation-fenced sprite
bindings own their own asset leases, release late results, and clear on reuse/hide/disposal.
No item enters UIManager's context pool, and the adapter never shuts down the borrowed
resource service. Unexpected external root destruction defers prefab release one frame;
normal owner-driven disposal is synchronous. See [Virtualization.md](Virtualization.md).

`ResourceOwnershipEditModeTests` covers keys and the package registry.
`ResourceOwnershipPlayModeTests` covers single-flight, per-waiter cancellation, abandoned-load
release, package/location/type separation, unknown packages, asset/instance separation and
idempotent disposal, preload cache hits, batch partial failure, low-memory trimming, leak
snapshots and shutdown draining.
`UIManagerResourceOwnershipPlayModeTests` covers open, cancel, load failure, pooling, clear pool
and shutdown lease accounting.

All of them use a fake `IUIResourceProvider`, so they need neither YooAsset nor a network.

## Known limitations

**Cancellation is observed before rollback completes.** The stage 3
`UIOperationCoordinator` surfaces `OperationCanceledException` to the caller as soon as the
last waiter leaves; it does not wait for the queued operation body to unwind. If an open is
cancelled *after* `UIManager` has already registered the instance lease — which is possible
once a show transition is running, because the transition is the next suspension point — then
`await OpenAsync(...)` throws while the lease is still held for one or more frames.

The lease is returned deterministically when the operation body unwinds
(`ReleaseContextInternal` → `ReleaseOwnedInstanceLease`), and `ShutdownAsync` drains in-flight
operations before its sweep, so this is **not** a leak and never double-releases. But it does
mean the returned `OperationCanceledException` is not a synchronization point for resource
release: code that must observe a settled reference count should poll `GetDiagnostics()` or
await `ShutdownAsync`. `CancelDuringShowTransition_ReturnsLeaseOnceOperationUnwinds` pins this
behavior. Making cancellation synchronous with rollback would change the stage 3 coordinator
contract and is deliberately left out of stage 5.

**Externally destroyed pooled instances.** Stage 7 no longer silently drops these entries.
The pool records an invalid-entry eviction and passes the entry to `UIManager`, which runs
terminal context cleanup and returns the orphaned instance lease. Without that finalization
the reference count for the asset could never reach zero.

## Out of scope for stage 5

- Resource update bootstrap (EditorSimulate/Offline/Host, fallback, reset) — stage 6.
- Pooling scopes are ownership identities only; stage 12 scene/system UI services remain out
  of scope.
