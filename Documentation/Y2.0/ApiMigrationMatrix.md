# Y1 to Y2 API Migration Matrix

This matrix records the implemented Y2 contracts and retained compatibility
surfaces. All retained obsolete facades/aliases now name scheduled removal
**2.1.0**. Follow [Migration.md](Migration.md) and
[PackageQuickstart.md](PackageQuickstart.md) for an executable installed example.

| Y1 surface | Y2 direction | Compatibility |
|---|---|---|
| `UIManager.Instance` | Injected `IUIService` | Temporary facade; Y2 service available |
| `UIManager.Init` | `Initialize` / `InitializeAsync` / `ShutdownAsync` | Obsolete forwarding overload implemented |
| `Register<T>(UIConfig)` | Excel `UISettings` + explicit `UIConfigRegistration.For<T>` + `UIManager.RegisterBatch` | Manual mutable registration remains compatible; new batch inputs are validated copies |
| Hardcoded text / source static localization utility | Excel `LocalizationTextConfig` + instance `TextLocalizationService` | `Get/TryGet` are strict; `Resolve` exposes explicit fallback/error status |
| Source Unity Localization string/sprite event | `ConfigLocalizedText` key picker + `ConfigPresentationBinding` typed font/sprite tokens | Original instance DI; no Addressables or source-game singleton |
| Per-page hardcoded scaler/safe insets | `UIRootRuntimeOptions.ScalePolicy` + `UISafeAreaContent` | Content only; blockers/backgrounds remain fullscreen |
| Ad-hoc theme/font changes | `UIPresentationService` + immutable `UIPresentationCatalog` | Explicit resource leases; baseline restoration and no cumulative scaling |
| Custom accessibility animation writer | `UIAccessibilityOptions` + existing `UITransitionRunner.SetReducedMotion` | No second tween/state engine; focused semantics/formatter/RTL extension points |
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
| Implicit reflected Context construction failure during Open | `UIConfigRegistration.ValidateContextType` before Register/batch | Concrete closed class + public no-arg constructor; both generic Open and Type-based prewarm remain reflected |
| Ad-hoc DLL loading before resource readiness | Optional AOT `ResourceHotfixCodeLoader` + `HotfixGameEntry` | Existing Bootstrap order, trusted identity/hash, borrowed leases and injected mock/Mono/future adapter; no installed HybridCLR |
| Preserve treated as universal AOT generic support | Explicit sample roots + `HotfixAotWitness` + metadata-owner draft | Linker retention, concrete instantiations and future target/base-player-matched metadata are separate; see HybridCLRBoundary.md |
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
| Mutable `UIConfig` fields | `ConfigService` typed snapshots and copied batch input | No global freeze or automatic active re-registration; see `Config.md` |
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
| Ad-hoc scene cleanup | `IUIService.Scopes` Global/Scene/Module lifetime owners | Reuses UIPoolScope identities; lifetime Release closes active UI/tasks; old pool Release stays idle-only |
| Global-only page navigation | `UINavigateOptions.Scope` / `UIScope.PushAsync` | Existing FIFO/guards; default remains Global |
| Gameplay input independent of UI | `IUIService.InputArbiter` | Uses existing locks/modal/fullscreen/focus/pointer state; scene controllers opt in |
| Loading show/hide pairs | `UILoadingService.Begin` independent progress/cancellation handles | Last handle controls visibility; scope/caller cancellation removes only its handle |
| Unbounded Toast list | `UIToastService.Enqueue` + unscaled Tick | Capacity/rate/merge/priority and explicit completion outcomes |
| Callback-only confirmation | `UIConfirmationService<T>` / `UIConfirmationSession` | FIFO typed result; cancellation is not Declined; external close is Dismissed |
| Bespoke disconnected/maintenance/update UI | `UISystemUIService<T>` | Global UI and explicit decisions; no network/store implementation |
| Ad-hoc debug globals and resource-count guesses | Optional injected `UIDiagnostics`, owned `UIProfilerSampler` | Existing state remains authoritative; bounded immutable snapshots, explicit owner leases and exact typed duplicate detection |
| Uncorrelated operation logs | Parent-linked lifecycle/Load/NativeLoad/Lease/Transition spans + `IUIDiagnosticsAdapter` | Explicit safe-boundary flush, adapter quarantine, monotonic execution timing; no SDK |
| Editor FPS treated as a mobile result | `UIPerformanceBudget` / `UIPerformanceReport` | Actual target/scenario/environment/thresholds; unsupported is Unavailable and unmeasured mobile is NotValidated |
| Runtime control-name searches / hand-edited generated code | `UIPageGenerator` serialized typed controls and owned `.g.cs` + hand partial | Source prefab and existing hand code remain unchanged; changed generated files, identity or source structure require explicit reconciliation |
| Ad-hoc page setup and handwritten registration duplication | Additive `UISettings` row, existing exporter and generated `Registration.Map(snapshot)` | Same typed Config pipeline and `RegisterBatch`; no runtime type scan or second registry |
| Implicit permissive Player builds | Explicit `UIBuildProfile` + `UIBuildGate` | Missing/incomplete/error profiles block; Production wrapper audits actual build scenes; audited MinimalDemo is not Production |
| Scene-only Resources ignore list | Strict Production resource policy + `UIBuildWorkspace.Export` | Explicit external copy excludes audited demo inputs, preserves source login/demo assets, then requires revalidation |
| Separate mock device/theme preview | Existing `UIDevicePreview` + `UIEditorPresentationPreview` | Real Config assets and deep transient TMP graphs; no source writes or runtime input locks |
| Manual YooAsset report/version bookkeeping | `UIYooAssetBuilder.Build` and append-only external history | Official Legacy builder, actual files/SHA256, explicit target/module/cancel limitations; no upload or SDK |
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
- The retained obsolete surface is scheduled for 2.1.0, not an unspecified migration window.
- Package Editor depends on pure generated Config, not the optional business samples.
- New samples and documentation use only Y2 APIs.
- Breaking behavior changes require a migration note and a regression test.
