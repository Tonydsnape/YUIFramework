# Y2 Resource Bootstrap Migration

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

Use **resource bootstrap** or **resource update** for stage 6. `IBootstrapCodeLoader` is only
an extension point. HybridCLR/code hot update is not installed or implemented in this stage.
