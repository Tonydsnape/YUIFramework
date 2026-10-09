# Y1 to Y2 API Migration Matrix

This matrix records intent before implementation. Final Y2 names may be refined during the public-contract phase, but ownership and behavior requirements are fixed here.

| Y1 surface | Y2 direction | Compatibility |
|---|---|---|
| `UIManager.Instance` | Injected `IUIService` | Temporary facade; Y2 service available |
| `UIManager.Init` | `Initialize` / `InitializeAsync` / `ShutdownAsync` | Obsolete forwarding overload implemented |
| `Register<T>(UIConfig)` | Registry with validated immutable descriptor | Temporary adapter |
| `OpenAsync<T>(object)` | UniTask/cancellation now; typed request later | `OpenHandleAsync<T>` added, object arguments retained |
| `CloseAsync<T>()` | UniTask/cancellation and owning-service handle | Existing overload retained |
| `UINavigator` | `IUINavigator`; command queue and transactions next | Interface and cancellation implemented; stage 3 adds FIFO queue, `IsBusy`, `BringToTopAsync<T>`, `NavigateBackAsync`, guards, and transaction rollback |
| `IResourceLoader` | `IUIResourceService` with `UIResourceKey` + asset/instance leases | Legacy interface and `UIManager.Initialize(IResourceLoader)` retained unchanged; stage 5 ownership is opt-in via `UIManager.Initialize(IUIResourceService)` |
| `ResourcesLoader` | `ResourcesResourceProvider` (tests/Editor only) | Legacy loader retained; the provider obeys the same lease rules |
| `AddressablesLoader` | Removed in stage 5 | Type, `YUIFRAMEWORK_ADDRESSABLES` version define, and docs deleted; no Y2 compatibility |
| `YooAssetLoader` | `YooAssetBootstrapComposition.ResourceService` | Obsolete forwarding adapter retained; new `YooAssetResourceProvider` lives in `YUIFramework.Bootstrap.YooAsset` and has no manager dependency |
| `loader.Release(key, instance)` (destroy + unref in one call) | `IUIInstanceLease.Release()` / `IUIAssetLease.Release()` | Asset and instance ownership are now separate and idempotent |
| `HotUpdateConfig` mutable statics | Immutable `BootstrapProfile` | Obsolete properties rebuild the one legacy profile; new code owns a profile instance |
| `HotUpdateManager.Instance` | Injected `BootstrapRunner` + `IBootstrapBackend` | Obsolete stateless singleton-shaped facade forwards to the one legacy runner |
| `HotUpdateLauncher` static run/events | `BootstrapRunner`, `IBootstrapProgressSink`, `IBootstrapTelemetrySink` | Obsolete forwarding facade clears all callbacks at subsystem registration |
| `RemoteServices` static URL composition | `YooAssetBootstrapBackend` endpoint-scoped resolver | Obsolete facade only; Y2 path is application/channel/version/platform/package scoped |
| `StartupFlowTrace` static sequence | Injected structured telemetry | Obsolete log facade has no retained sequence/state |
| resource "hot update" terminology | resource bootstrap/resource update | Code loading is the independent `IBootstrapCodeLoader` extension point |
| String message names | `UIMessageTopic<T>` + `IUIMessageBus` | `[Obsolete]` string overloads forward to the same typed channel engine |
| Owner-only message cleanup | `UIMessageScope` or context lifetime/display helpers | Owner removal retained for the compatibility window |
| Ad-hoc button callbacks | `UICommand` / `UIAsyncCommand` + `UIDataBinding.BindButton` | Awaitable failures/cancellation and CanExecute are now explicit |
| Mutable-only observable property | `IReadOnlyObservableProperty<T>` / `IObservableProperty<T>` / `ValidatedProperty<T>` | `ObservableProperty<T>` implements the new interfaces |
| `UIVirtualList` standalone fixed-size kernel | Optional `SuperScrollViewList<T>` commercial List/Grid backend | Obsolete facade forwards to the installed adapter; no fallback kernel or hard vendor dependency |
| Index-only item binding | `UIListDataSource<T>` + stable IDs + `UIListItemBinding` | Generation/token fences and asset-lease cleanup are required for async item results |
| Collection add/remove/reset only | `Insert`, `Replace`, `Move`, precommit validation, postcommit aggregate notifications | Reentrant collection mutation is rejected; duplicate list IDs fail before commit |
| Legacy list padding/end alignment | Parent viewport padding / typed leading scroll offset | Unsupported legacy options throw; see `Virtualization.md` |
| Context-owned ViewModel by assumption | `SetViewModel(vm, UIViewModelOwnership)` | Default remains `Owned`; borrowed instances opt into `External` |
| `Task` runtime APIs | `UniTask` + `CancellationToken` | Runtime migration implemented |
| Mutable `UIConfig` fields | Validated descriptor/config asset | Import/conversion helper |
| `DefaultLayer` plus config layer | Single authoritative layer source | Resolve during contract phase |
| `PreloadCount` placeholder | `UIManager.PrewarmAsync<T>` initialized-instance target | Requires caching/capacity; distinct from resource preload |
| Per-type stack pool | Owned entries with per-key/global capacity, priority/LRU, expiry and scopes | Existing `CacheOnClose`/`MaxPoolSize` remain authoritative |
| Context-only lifetime cancellation | Permanent `LifetimeToken` plus per-display `DisplayToken` | Existing lifetime helpers remain permanent; new display helpers clean on pooling |
| Direct transition helper calls | `IUIService.Transitions` plus `RequestTransitionInterruption` | `UIManager.TransitionRunner` remains an obsolete forwarding property during the migration window |
| Hard-coded fade/scale/slide endpoints | Captured stable alpha/scale/anchored-position baseline | `RefreshTransitionBaseline` updates the baseline after intentional layout/binding changes |
| Lifecycle-only visibility | Orthogonal `BaseContext.VisibilityState` flags | Existing lifecycle states remain unchanged; interaction remains centrally composed |
| `UIContextState.None` | `UIContextState.Unloaded` | Legacy alias; same numeric value |
| `UIContextState.Shown` | `UIContextState.Opened` | Legacy alias; same numeric value |
| `UIContextState.Closed` | `UIContextState.Pooled` | Legacy alias; closed-and-releasable is now `Released` |
| `UIContextState.Destroyed` | `UIContextState.Released` | Legacy alias; same numeric value |
| Fail-fast per-context `OperationInProgress` (stage 2) | Per-key FIFO queue + `UIOperationReentrancyException` (stage 3) | Public same-key concurrent calls now queue instead of throwing; the stage 2 `UIOperationInProgressException` guard remains as a last-resort safety net |
| `UINavigateOptions.BringExistingPageToTop` | Always-on duplicate prevention | Retained for source compatibility; a duplicate Push/Replace target is always brought to the top regardless of this flag |
| `UIRoot.Instance` auto-find/empty creation | Explicit `UIRootRuntime.CreateOwned` / `CreateExternal` | Obsolete getter only; never searches or creates |
| Numeric `UILayer` sorting | Validated ten-layer `UILayerProfile` | `Bottom` aliases `Background`; `Top` aliases `Toast`; legacy values remain source/binary compatible |
| Monotonic sorting cursor | Bounded `UISortingLease` | Hidden retains; pool/release/failure disposes; `BringToTop` compacts |
| Global input boolean | `UIInputLockService.Acquire` lease | Multiple owners and whitelist intersection |
| Sample `Update` Escape handling | Runtime `UIInputRouter` | Routes to `NavigateBackAsync`; busy/in-flight deduplication |

## Lifecycle callback timing

| Callback | Y2 execution phase |
|---|---|
| `OnInit` / `HandleInit` | New instances only, during `Initializing`, after runtime binding and before `Opening` |
| `OnShow` / `HandleShow` | During `Opening`, before the show transition and before state becomes `Opened`; runs for new, active, and pooled opens |
| `OnHide` / `HandleHide` | During `Hiding`, after the hide transition on normal close; may also run during rollback before `Hidden` |
| `OnClose` / `HandleClose` | During `Closing`, after `Hidden`, with `CloseDisposition` already set to `Pool` or `Release` |
| `OnDestroy` / `HandleDestroy` | During `Releasing`, once for initialized contexts, before `LifetimeToken` cancellation and `Released` |

## Compatibility policy

- Compatibility code forwards to one Y2 implementation and never owns separate state.
- Compatibility warnings identify the replacement and planned removal version.
- New samples and documentation use only Y2 APIs.
- Breaking behavior changes require a migration note and a regression test.
