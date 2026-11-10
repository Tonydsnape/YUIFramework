# Y2 public API guide

Public runtime APIs are instance-owned and main-thread driven unless a particular
contract explicitly says otherwise. Do not retain internal mutable registries,
reflect into manager state, scan business types at startup or inject an Editor
assembly into a Player. The package Editor assembly no longer references business
Examples/Hotfix assemblies; optional sample tools resolve their entry only when
explicitly invoked.

| Area | Entry / output | Ownership and detailed contract |
|---|---|---|
| Startup | `BootstrapProfile`, `BootstrapRunner.RunAsync`, `IBootstrapGameEntry.EnterAsync` | AOT composition root owns the runner; entry runs after actual resources/code readiness; [Bootstrap](Bootstrap.md) |
| Production resources | `YooAssetBootstrapComposition`, `IUIResourceService`, `UIResourceKey.Of<T>`, `LoadAssetAsync<T>` | Borrow ready packages; independently dispose returned asset/instance leases before resource service/runner shutdown; [Resources](Resources.md) |
| UI | `new UIManager`, `InitializeAsync`, `RegisterBatch`, `OpenAsync<T>`, `OpenHandleAsync<T>`, `CloseAsync<T>`, `ShutdownAsync` | One service/root authority; handles are owner-bound; copied validated registration inputs; [Contracts](Contracts.md) |
| Context | `BaseContext`, `BasePageContext`, `BaseDialogContext`, `LifetimeToken`, `DisplayToken` | Concrete closed class with public no-arg constructor; `HandleInit/Show/Hide/Close/Destroy` callbacks; [Lifecycle](Lifecycle.md) |
| Navigation | `ui.Navigator.PushAsync/ReplaceAsync/BackAsync/BringToTopAsync` | FIFO transactions and rollback; immutable diagnostic stack; no duplicate page identity; [Navigation](Navigation.md) |
| Root/input | `UIRootRuntime.CreateOwned/CreateExternal`, layers, focus, `InputLocks.Acquire`, `InputArbiter` | Explicit root/EventSystem ownership; lease locks; no extra CanvasGroup/EventSystem writer; [UIRootAndInput](UIRootAndInput.md) |
| Configuration | `ConfigService.InitializeAsync`, `ConfigSnapshot.Get`, `UISettingsMapping.Convert`, `UIConfigRegistration.For<T>` | Immutable typed publication, explicit row-to-Context map and atomic batch; reload does not silently re-register active UI; [Config](Config.md) |
| Messaging | `UIMessageTopic<T>`, `IUIMessageBus`, `UIMessageScope` | Typed topic + exact owner/handler; display/lifetime tokens automatically detach; [Messaging](Messaging.md) |
| MVVM | `ViewModelBase`, `ObservableProperty<T>`, `ObservableCollection<T>`, `UICommand/UIAsyncCommand`, `UIDataBinding` | Owned/external VM distinction; track bindings by display or lifetime; no polling; [MVVM](MVVM.md) |
| Pool | `PrewarmAsync<T>`, `PrewarmRegisteredAsync`, `ClearPoolAsync<T>`, `PoolDiagnostics` | Initialized-instance targets, bounded idle cache, separate resource preloading; [Pooling](Pooling.md) |
| Transition | `ui.Transitions`, registered custom transitions, reduced-motion policy | Single writer; baseline/reversal/cancel semantics; [Transitions](Transitions.md) |
| Scene/module | `ui.Scopes.CurrentScene/ForScene/CreateModule`, `UIScope.OpenAsync/RunAsync/ReleaseAsync` | Lifetime scope closes active UI and drains work; old pool scope release stays idle-only; [SystemUI](SystemUI.md) |
| System overlays | `UILoadingService.Begin`, `UIToastService.Enqueue/Tick`, confirmation/system services | Independent reference handles, bounded queue and typed results/cancellation; [SystemUI](SystemUI.md) |
| Text/theme | `TextLocalizationService`, `ConfigLocalizedText`, `UIPresentationService`, `UISafeAreaContent` | Event-driven locale/accessibility, scoped fonts/sprites/material leases, baseline restoration; [Localization](Localization.md), [Presentation](Presentation.md) |
| Diagnostics | `UIDiagnostics`, adapters, `UIProfilerSampler`, budget/report types | Opt-in bounded events, on-demand snapshots; observer isolation; unsupported is Unavailable; [Diagnostics](Diagnostics.md) |
| Editor/CI | `UIPageGenerator`, `UIAssetValidator`, `UIBuildGate`, `UIYooAssetBuilder`, package/RC tools | Editor-only, explicit scan scope/policy, locatable errors and real build reports; [EditorTooling](EditorTooling.md), [PackageQuickstart](PackageQuickstart.md) |
| Future code loading | `IBootstrapCodeLoader`, CodeLoading adapters, `IHotfixEntry` | AOT owns startup; no business/HybridCLR Runtime dependency; [HybridCLRBoundary](HybridCLRBoundary.md) |

## Minimal business Context and explicit registration

```csharp
public sealed class InventoryPage : BasePageContext
{
    protected override void HandleInit()
    {
        SetViewModel(new InventoryViewModel());
        // Use generated/serialized control references, not per-frame Transform.Find.
    }
}

ui.RegisterBatch(new[] {
    UIConfigRegistration.For<InventoryPage>(
        UISettingsMapping.Convert(snapshot.Get(UISettings.Table).Get("game", "Inventory")))
});
var page = await ui.Navigator.PushAsync<InventoryPage>(request, cancellationToken: token);
```

The application must first supply the actual table row, source and resource
address. Generic Open and Type-based prewarm both use reflected construction.
Do not mistake `new InventoryPage()` elsewhere or an Editor test for IL2CPP
retention/code-generation evidence.

Await each UniTask once. `Preserve()` in the installed UniTask version is not a
shared-pending-continuation promise. When startup is both observed and drained on
destruction, consume it once with `AsTask()`, store that Task and create independent
`AsUniTask()` wrappers for observers. Cancel, await the actual run, then release
UI/resources exactly once; do not suppress a noncooperative operation as cleaned.

Cancellation and faults are distinct from successful close/decline. Surface
failures, keep cleanup running across independent owners, then aggregate errors.
Do not call Shutdown on borrowed global services. Destroying a context cancels
its lifetime; hiding/pooling cancels display. A canceled close rollback restores
Opened and retains the display contract.

## Performance and compatibility

Cache/prewarm, async production loading, bounded diagnostics and event bindings
are mechanisms, not universal FPS/GC guarantees. Budget reports must name target
device, scene, threshold and measurement window. Historical local allocation
assertions are not full-frame measurements; Editor/Null render samples are not
Android/iOS certification. See Diagnostics/Virtualization/Testing.

All retained obsolete Y1 facades/aliases now state **scheduled removal 2.1.0**.
They remain because serialized GUIDs and incremental Y1 callers still use them;
deleting them now would invalidate the migration rather than tighten the API.
No replacement global registry or transitional runtime implementation is added.
Already-removed Addressables support and the independent legacy list kernel stay
removed. `IResourceLoader` remains a supported explicit compatibility/test adapter,
not the production resource-ownership API. See [ApiMigrationMatrix](ApiMigrationMatrix.md).
