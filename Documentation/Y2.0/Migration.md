# Y1 to Y2 migration (including resource Bootstrap)

## Upgrade a Y1 Hello example in a separate consumer

Back up scenes/prefabs and their `.meta` files, Excel inputs, handwritten Contexts
and application composition code. Work in a new consumer/upgrade copy, never
delete the active author's Assets or regenerate a user's dynamic font to make a
package gate pass. Follow [PackageQuickstart](PackageQuickstart.md) for export,
nonembedded installation, dependency lock, sample Import and testables.

1. Do not compile the old `Assets/YUIFramework` library beside the package. Keep
   business scenes/prefabs/scripts in an application folder. Runtime MonoScript
   GUIDs are retained by export; the optional examples become Assets only through
   actual sample Import. Preserve serialized references during the move.
2. The historical `HelloUIBootstrap`/`SampleHelloPage` names remain available
   after Import, but their implementation uses an owned `UIManager`, the typed
   Config snapshot/map and lifecycle bindings. In the clean demo consumer,
   explicitly create the sample's missing presentation assets once, then put
   `HelloUIBootstrap` on an empty scene object. Do not attach the complete-flow
   bootstrap simultaneously: each owns a root/EventSystem.
3. Replace `UIManager.Instance.Init(loader)` in business startup with an owned
   instance and awaited `InitializeAsync`; inject `IUIService` into business
   objects. Preserve the existing registration row values. Put each actual
   profile/ID into `UISettings`, convert its typed row and register the explicit
   Context map in one validated batch. Ship the data through the application's
   source; the demo's explicit Resources is not a production migration shortcut.
4. Contexts must be concrete classes with public parameterless constructors.
   Move control discovery into generated/serialized references during Init.
   `HandleShow` can run again with null navigation arguments: retain the original
   view model/dependencies where appropriate rather than overwriting them with
   null. Track display bindings and retain externally owned VMs explicitly.
5. Replace string topics with `UIMessageTopic<T>`, old global input toggles with
   lock leases, manual page stacks with `IUINavigator`, and scene teardown with
   `UIScope.ReleaseAsync`. Preserve canceled-close rollback semantics. For
   lifecycle-state aliases use the new names; notably old `Closed` means
   **Pooled**, not Released.
6. Replace production `ResourcesLoader`/`YooAssetLoader` usage with actual
   ready-package resources/leases through Bootstrap as below. Keep resource and
   instance ownership separate. Close/drain UI before disposing localization,
   Config/resources and the runner. Code-built demo loaders must also dispose
   their owned templates after UI shutdown.
7. Run the imported migrated-Hello regression, complete flow and package tests,
   then the application's scenes, strict Production profile, native build and
   actual device smoke. Confirm no display subscriptions/locks/tasks/templates
   remain. Reconcile resources/fonts/addresses explicitly; do not make a failing
   production profile pass by labeling the real application MinimalDemo.

`MigratedHelloSampleTests.MigratedY1HelloSceneUsesConfigInstanceUiAndDrainsTemplates`
executes the retained Hello entry in the installed consumer, verifies its real
localized view and template cleanup. It is the executable example for these
steps; it is not a claim that arbitrary unprovided Y1 games migrate automatically.
The complete sample separately exercises the consolidated Y2 contracts.

## Compatibility removal schedule

Retained obsolete facades, string message helpers, layer/state aliases,
`UIManager.Instance/Init/TransitionRunner`, `UIRoot.Instance`, the legacy layer
constructor, `BringExistingPageToTop`, list facade and resource-startup wrappers
are scheduled for **2.1.0** removal. Warnings name replacements and that version.
They stay in 2.0 RC to preserve serialized GUIDs and incremental Y1 callers, not
as a second implementation. Supported explicit `IResourceLoader` compatibility
is not silently removed. The already-removed Addressables path and old standalone
list kernel are not restored. See [ApiMigrationMatrix](ApiMigrationMatrix.md).

Do not use the installed package's schema namespace for newly generated
application schema types; see the no-duplicate-schema authoring steps in the
package guide. Upgrades are explicit new exports and consumer dependency changes,
not overwrites of installed or handwritten files.

## Replace static startup configuration

Replace writes to `HotUpdateConfig` with one immutable `BootstrapProfile` built at the
composition root. Do not mutate global play mode, package, CDN, retry, or timeout fields.

```csharp
var profile = new BootstrapProfile(
    BootstrapMode.Host,
    new[] { new BootstrapPackageProfile("DefaultPackage") },
    applicationId,
    channel,
    applicationVersion,
    primaryCdn,
    fallbackCdn);
```

## Replace manager/launcher singletons

Replace `HotUpdateManager.Instance` and `HotUpdateLauncher.RunAsync()` with an owned
`BootstrapRunner`. Inject `IBootstrapBackend`, `IBootstrapGameEntry`, and optional policy and
observability contracts. Keep the runner alive until application shutdown.

The old types remain `[Obsolete]` forwarding facades and preserve their `.cs.meta` GUIDs for
existing scenes. They share the Bootstrap state/result and never claim success after a failed
fallback. New code must not depend on them.

## Replace YooAssetLoader

Create resource services from the ready context:

```csharp
var composition = YooAssetBootstrapComposition.Create(result.ReadyContext);
await ui.InitializeAsync(composition.ResourceService, cancellationToken: token);
```

`YooAssetResourceProvider` now lives in `YUIFramework.Bootstrap.YooAsset`; its original
MonoScript GUID is preserved. It still receives an explicit `ResourcePackage` and has no
manager/singleton dependency.

## Move business entry behind readiness

Registration, first-page navigation, and other business startup belong in
`IBootstrapGameEntry.EnterAsync`. They cannot run before `ResourcesReady` and the optional
`IBootstrapCodeLoader` has succeeded.

## Shutdown in ownership order

1. Stop and shut down UI services.
2. Shut down the `YooAssetBootstrapComposition` resource service.
3. Shut down the `BootstrapRunner`.

Do not destroy YooAsset packages while asset leases are still held.

## Terminology

Scene/system-UI owners can migrate incrementally to `ui.Scopes` and independent
Loading/Toast/confirmation services; see [SystemUI.md](SystemUI.md). Keep
ReleaseScopeAsync(UIPoolScope) for idle-only pool governance, use
UIScope.ReleaseAsync for active scene/module teardown, and await it before
disposing scene-owned resources. Existing default opens/navigation stay Global.

Use **resource bootstrap** or **resource update** for stage 6. `IBootstrapCodeLoader` is only
an extension point. HybridCLR/code hot update is not installed or implemented in this stage.
