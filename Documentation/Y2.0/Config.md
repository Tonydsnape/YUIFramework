# Typed Config integration

This independent post-stage-10 addition integrates the user-owned MatchingGo Config
protocol. It does **not** begin stage 11, add themes/localization, or install HybridCLR.
The source game is read-only. Reused components are the generic Excel parser/error
model, JSON/MessagePack layout, code-generator structure, validation/report utilities and
ConfigValue/key helpers. GameConfig statics, CardCollect phases, game repositories,
payments, seed data, workbooks, generated tables and the source export batch are excluded.
The target has its own tool manifest/lock; no source `.git` or `node_modules` is copied.

## Pipeline

`Excel -> validation -> JSON + MessagePack + typed C# -> YooAsset -> ConfigService
-> explicit Context map -> UIManager.RegisterBatch -> business entry`

See [`Config/README.md`](../../Config/README.md) for commands and the workbook protocol.
Client publication stages all six output directories before swapping any. A failed
validation/codegen/swap leaves the prior valid publication intact; all six injected
swap failures are covered. Readers must not import mid-swap. This does not promise
crash recovery or concurrent-editor isolation.

The generated `UISettings` table uses `(Profile, Id)` keys to retain different
configurations for the same context in independent startup examples. All UIConfig
data fields are represented: prefab/package, layer, cache/capacity/prewarm/priority/expiry,
fullscreen/modal policy, transition type/durations/time scale/slide/scale/custom ID,
baseline refresh and covered suspension. AnimationCurve and custom transition code
remain explicit application overrides; spreadsheets never embed Unity objects.

## Runtime API and ownership

`YUIFramework.Config` depends on Runtime, UniTask and the pinned codecs, not a game
singleton, game assembly or old HotUpdate manager. The separate `Config.Editor` assembly
contains the only AssetDatabase access. Runtime does not perform filesystem I/O.

```csharp
var configs = new ConfigService(
    new ResourceConfigSource(resources, package: "DefaultPackage"),
    new ConfigCodec(), ConfigFormat.MessagePack, GeneratedConfigCatalog.Tables);
var snapshot = await configs.InitializeAsync(cancellationToken);
UISettings table = snapshot.Get(UISettings.Table);
UISettingsRow page = table.Get("hello", "HelloPage");
bool found = table.TryGet("hello", "HelloPage", out var row);
```

Generated tables/rows have instance read-only properties and collections; no mutable
static table state or reflection-based type discovery. JSON-valued properties clone
their JToken. Stable descriptor identity (`UISettings.Table`) selects a typed table.
Custom `ConfigTable<T>` parsers must likewise return immutable, independently owned
objects; the service cannot make an arbitrary caller type immutable.

- Construct/use the service and source on its Unity owning thread. Custom asynchronous
  sources must resume there. Configuration is loaded at startup/reload, not polled per frame.
- Initialize and reload each join the current single flight. A caller token cancels only
  that caller's wait. Canceling all callers does not silently abandon the owner's load.
- The entire requested table set is parsed locally, then published by one snapshot reference
  assignment. Each source asset/lease is disposed **before** its table becomes eligible.
- A required-table failure aborts publication. Retry may start a fresh run. Reload failure
  retains the last valid snapshot; existing readers keep that snapshot after successful reload too.
- Optional failures are returned in `snapshot.OptionalFailures`; the failed table is absent,
  including on disposal failure. `ConfigUIStartup` deliberately refuses any optional failure.
  There are no game-specific Startup/Deferred phase lists.
- Load errors are propagated with table context and retained in `LastFailure`, including late
  faults after a caller canceled its wait. Completion outcomes are observed, not unobserved
  fire-and-forget faults.
- Shutdown invalidates the generation, clears the published snapshot, cancels and drains
  the owner run, and prevents initialization until drain finishes. A late noncooperative
  source cannot publish or resurrect a previous run. Reinitialization is then permitted.
  A source that never completes can still delay drain; arbitrary work cannot be forcibly stopped.
- Sources, resource service and descriptors are borrowed. Config never shuts down injected
  resources. `ResourceConfigSource` releases every TextAsset lease, including failure/cancel.
  Zero-reference resource cache may remain until its owning service trims/shuts down.
- Codec limits reject oversized, trailing, duplicate-key, excessively deep, nonfinite and
  unsupported extension/binary payloads. This is a data codec, not an authenticity check:
  trusted manifest/package verification still belongs to Bootstrap/YooAsset.

## Batch registration and retained compatibility

```csharp
await ConfigUIStartup.EnterAsync(configs, ui,
    snapshot => SampleUIConfiguration.Map(snapshot, "hello"),
    async token => { await ui.Navigator.PushAsync<SampleHelloPage>(
        "Hello", cancellationToken: token); },
    cancellationToken);
```

`UIConfigRegistration.For<T>` is the explicit typed mapping primitive. `RegisterBatch`
validates every candidate and the live layer profile before mutation: duplicate type or
ordinal UI ID, pre-existing type/identity, invalid numeric fields/enums, bad prewarm bounds,
empty package/custom identifiers, and invalid custom-transition selection all fail with
zero registrations. Explicit prefab packages must resolve in the injected resource
service; batch registration rejects them with a legacy package-unaware loader.
Types cannot be automatically re-registered during reload, whether
active, pooled or merely registered. Apply new UI registries only at an explicit owner
shutdown/reinitialize boundary. Shared prefab addresses across different contexts remain legal.

`UIConfigRegistration` and batch insertion copy configuration and AnimationCurve objects.
The legacy `Register<T>(UIConfig)` and `TryGetConfig` mutable semantics are unchanged;
this is an isolated validated input snapshot path, **not** a global UIConfig freeze.
Runtime overrides run before copying/validation. Register custom transition implementations
with the runner explicitly before opening a custom-transition page.

## Existing registration migration checklist

| Original registration | New profile / table key | Preserved distinctions |
|---|---|---|
| HotUpdateStartupSample: SampleHelloPage | `bootstrap / HelloPage` | SampleHelloPage prefab, Normal, cache 1, fullscreen, transition disabled |
| HelloUIBootstrap: SampleHelloPage | `hello / HelloPage` | cache 1, fullscreen, Fade .2/.15 |
| HelloUIBootstrap: SecondSamplePage | `hello / SecondSamplePage` | no cache, fullscreen, SlideLeft .25/.2, distance 900 |
| HelloUIBootstrap: VirtualListSamplePage | `hello / VirtualListSamplePage` | cache 1, fullscreen, Scale .2/.15, start scale .92 |
| HelloUIBootstrap: MvvmSamplePage | `hello / MvvmSamplePage` | cache 1, fullscreen, Fade .18/.15 |
| Y2ServiceBootstrap: SampleHelloPage | `y2 / SampleHelloPage` | different ID, cache 2, prewarm 2, priority 10, expiry 120, Fade .2/.15 |

All six retain null/default prefab package, Normal layer, default modal policy, unscaled
timing, baseline refresh and covered suspension; full values are in the workbook and
reproducible creation script. There were no custom curves to migrate. The mapping helper
offers an explicit override before validation/copying for future curves/custom code.

All three bootstraps call the same ConfigUIStartup chain. Unknown/missing selected-profile
records or invalid records anywhere in the loaded UI table fail before registering any
selected UI. Test registrations intentionally remain manual to verify compatibility.
The existing Editor directory contains a BootstrapProfile editor, not a UI registration
code-generation template, so no hidden handwritten template remains.

The existing page buttons now address their owning `Services`, not the unrelated singleton.
Unavailable page mappings disable navigation buttons. Unity 2022.3 requires
`LegacyRuntime.ttf`, so the actual migrated example pages were corrected from `Arial.ttf`.
These are integration-enabling fixes, not new navigation/layout behavior.
The legacy virtual-list page remains a deliberate optional-plugin facade example; its UI
registration is migrated, but opening the list still requires the licensed backend.
It does not silently substitute another list kernel when the plugin is missing.

## Resource addresses, modes and startup order

`HotUpdateStartupSample.EnterAsync` runs only after `BootstrapRunner` has verified its
resource packages. It creates the YooAsset resource composition and UI owner, then loads
Config, batch-registers and finally navigates. Config/mapping failure prevents business
entry. The generic Bootstrap state machine does not require every game to use Config.

In EditorSimulate, the sample explicitly reads
`Assets/YUIFramework/ConfigData/Editor/json/UISettings.json` through EditorJsonConfigSource.
In Offline/Host and Player, it reads the MessagePack TextAsset through the verified resource
service at **packageName + address `UISettings`**. Configure YooAsset's collector for
`Assets/Resources/YUIConfig/UISettings.bytes`, address rule filename-without-extension,
in that package; JSON and generated C# are not bundle assets. If the collector uses another
address convention, set `ResourceConfigSource`'s explicit prefix/suffix/package accordingly.

HelloUIBootstrap and Y2ServiceBootstrap explicitly use Editor JSON in Editor, and
`ResourcesResourceProvider` at `YUIConfig/UISettings` in standalone minimal CodeView demos.
That Player demo provider is not a production recommendation. There is no catch-and-fallback
to handwritten config. Production apps should move/configure their bytes outside Resources
if avoiding built-in duplication, and collect them with YooAsset; the fixed default sync
location is for this repository's minimal samples.

Owner teardown is **UI -> Config -> resource service -> Bootstrap backend**. The production
sample first awaits its canceled startup task to prevent late entry/reinitialization during
teardown, then attempts each shutdown even if another fails. Config's own shutdown does not
depend on UI state or end active UI scopes. Do not dispose external models/services.

## Evidence and limits

See [Testing.md](Testing.md) for exact XML/test-run timestamps and copy manifests.
Tests exercise original protocol/long/compound keys, real generated UI tables, resource
leases, cancellation/retry/reload/generation fencing, all six swap rollback points, and
actual migrated pages plus verified fake-Bootstrap success/failure composition.
MessagePack payloads generated by the Node tool are parsed in Unity, not a mock codec only.

No real CDN, device build, IL2CPP/HybridCLR stripping or 60-FPS measurement is claimed.
Config parsing allocates during load and validates the whole snapshot; it is not a
zero-allocation binding path. Existing stage9/10 GC tests remain separate regressions.
Commercial SuperScrollView code stays local/ignored and is not part of this toolchain.
