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
| `IResourceLoader` | UniTask/cancellation now; leases later | Signature migrated; ownership replacement pending |
| `ResourcesLoader` | Test/Editor compatibility only | Removed from production setup |
| `AddressablesLoader` | Removed | No Y2 production compatibility |
| `YooAssetLoader` | `YUIFramework.YooAsset` injected adapter | Replaced |
| `HotUpdateManager.Instance` | Injected Bootstrap service/profile | Temporary facade only if needed |
| String message names | Strongly typed topics/messages | String API temporarily obsolete |
| `Task` runtime APIs | `UniTask` + `CancellationToken` | Runtime migration implemented |
| Mutable `UIConfig` fields | Validated descriptor/config asset | Import/conversion helper |
| `DefaultLayer` plus config layer | Single authoritative layer source | Resolve during contract phase |
| `PreloadCount` placeholder | Implemented preload policy | Field maps to new policy |
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
