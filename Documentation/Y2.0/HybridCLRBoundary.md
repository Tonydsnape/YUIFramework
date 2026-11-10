# Stage 15: AOT / Hotfix boundary

This stage prepares **code-loading boundaries without installing HybridCLR**.
It is not a hot-update release, metadata generation, or Android/iOS device
certification. Stock IL2CPP cannot execute a newly downloaded managed DLL.
`MonoHotfixAssemblyRuntime` explicitly refuses that operation under
`ENABLE_IL2CPP`; a future separately installed adapter is required.

## Physical assembly map

| Role | Actual assembly / contract |
|---|---|
| AOT abstractions | Existing public `IUIService`, registry, Context and resource contracts in `YUIFramework.Runtime`; `IHotfixEntry` / `IHotfixAssemblyRuntime` in `YUIFramework.CodeLoading` |
| AOT runtime | `YUIFramework.Runtime`, optional Config/localization/presentation modules used by the application, `UniTask`, `UnityEngine.UI` |
| AOT resources | `YooAsset`, `YUIFramework.Bootstrap.YooAsset` |
| AOT startup | `YUIFramework.Bootstrap`, `YUIFramework.CodeLoading`; optional `YUIFramework.Hotfix.AotSample` |
| Simulated future business DLL | Independent `YUIFramework.Hotfix.Sample` asmdef, `autoReferenced: false` |
| Editor only | `YUIFramework.Editor`; never referenced by Hotfix.Sample |

“Abstractions” is a logical role here, **not an invented
`YUIFramework.Abstractions.dll`**. Extracting the already-public interfaces from
Runtime would require a broad circular-dependency/API migration and is not
necessary to establish this boundary. Runtime/Bootstrap/CodeLoading have no
reference to Hotfix.Sample. The separate AOT sample also locates the mock by
name rather than depending on its asmdef. No friend-assembly grant gives Hotfix
access to Runtime internals. The real compiled Player assembly graph is tested.

Hotfix.Sample depends only on public Runtime/CodeLoading, UniTask and uGUI APIs.
`HotfixSampleEntry.EnterAsync` calls `IUIService.Register<T>` / `OpenAsync<T>`;
its page uses the existing display subscriptions, async command, focus and
pool lifecycle. No runtime type scan, second registry, singleton, or synchronous
production resource loader is introduced. Its simple view is intentionally
code-built, not a production prefab/backend or a replacement for the login scene.

## Context construction and stripping are separate requirements

Every registered Context must be a **concrete, closed class deriving from
BaseContext with a public parameterless constructor**. `Register<T>` and
`UIConfigRegistration.For<T>` now validate that before publication; the Editor
Context mapping gate enforces the same rule. Invalid registrations fail earlier
instead of failing during load/prewarm. Keep constructors side-effect free.

Both existing construction paths remain:

- `UIManager.OpenNewAsync<T>` uses `Activator.CreateInstance<T>()`.
- `UIManager.CreatePrewarmedContextAsync` uses `Activator.CreateInstance(Type)`.

No factory registration overload is added. A future factory design must replace
**both** paths rather than only the direct new/open case.

`Hotfix.Sample/link.xml` and `[Preserve]` root the sample entry/page constructors
and business callbacks, plus the AOT witness. These are explicit sample roots,
not an all-application preservation policy. If the future externally loaded
Hotfix DLL is removed from base-player compilation, its types/link roots must
be reconciled with the installed HybridCLR workflow; do not blindly copy this
compiled-in mock's Player inclusion settings.

The [Unity 2022.3 stripping documentation](https://docs.unity3d.com/2022.3/Documentation/Manual/ManagedCodeStripping.html)
states that type-level Preserve retains the type and default constructor.
That preserves linker reachability; it **does not manufacture arbitrary IL2CPP
generic machine code**. Real High-stripping build/Player evidence and its
limitations are recorded in [Testing.md](Testing.md).

## Verified resources → assembly → business

Use the existing `BootstrapRunner`, not another startup state machine:

```csharp
YooAssetBootstrapComposition composition = null; // owned by this application
var spec = new HotfixAssemblySpec(
    "CodePackage", "Hotfix.dll.bytes", trustedAssemblyFullName,
    "MyGame.Hotfix.Entry", trustedSha256);
var code = new ResourceHotfixCodeLoader(spec, ready =>
{
    if (composition == null) composition = YooAssetBootstrapComposition.Create(ready);
    return composition.ResourceService;
}, new MonoHotfixAssemblyRuntime()); // Mono only; inject future adapter on IL2CPP
var game = new HotfixGameEntry(code, ready =>
{
    if (!ui.IsInitialized) ui.Initialize(composition.ResourceService);
    return ui;
}, copiedPageConfig);
// Supply code/game to the existing BootstrapRunner constructor.
```

In an actual application include the required Config/localization initialization
in the owning composition before business entry; the above omits those optional
services. `PreloadedHotfixAssemblyRuntime(knownAssembly)` is the explicitly named
fake implementation for an already-compiled assembly. It **does not execute
payload bytes** and must not be reported as a successful downloaded-DLL load.
The separate real Mono test compiles a fresh, not-preloaded DLL, imports binary
`.dll.bytes`, hashes it, loads its bytes and invokes its entry through Bootstrap.

The loader requires all ready manifests to be verified, the explicit code
package to belong to that ready context, an exact `Assembly.FullName`, entry
type and SHA256 from an application-trusted manifest. SHA256 is content identity,
**not a signature, trust bootstrap or sandbox**. Treat executable content as
trusted application code. YooAsset remains the only production resource backend.

The code loader borrows `IUIResourceService`; it neither disposes the owner nor
shuts down providers. A TextAsset lease is disposed **before executing assembly
or constructor code**. The resource service may legitimately retain a zero-lease
cache entry until owner trim/shutdown; zero leases does not mean native unload.
Canceled noncooperative provider loads retain their existing late-release drain.

Assembly loading/constructors are synchronous and irreversible. `LoadedAssembly`
records a completed load even if cancellation arrives immediately afterward.
A subsequent uncanceled call can finish entry construction without reloading.
Cancellation during a completed constructor retains `Entry`, avoids repeating
constructor side effects, and still throws cancellation to its caller.
Execution/identity/type/constructor faults after an assembly-load attempt are
sticky: the same loader refuses retries and preserves the exception chain.
Restart the loader/application deliberately; do not claim assemblies were unloaded.
Readiness/hash failures occur before executable loading and can be retried after
the application corrects its inputs.

`HotfixGameEntry` requires an initialized borrowed UI and a successfully loaded
entry, rejects concurrent/repeated entry, and returns the actual Context.
The application still owns UI/Config/resources/Bootstrap shutdown, in that order.
The entry adapter does not invent rollback for arbitrary Hotfix business effects.
`BootstrapRunner` already maps code faults to `CodeLoadFailed` and never starts
business entry after code failure. Its caller-wait versus run cancellation
semantics are unchanged.

## Three-layer generic risk register

| Surface | Linker preservation | Executed AOT witness | Future supplementation / remaining risk |
|---|---|---|---|
| Context / entry construction | Sample link.xml, Preserve, public no-arg ctor validation | Public entry, generic Open, Type-based prewarm, close and release | New reflected application types need explicit roots; preservation alone does not instantiate new generic combinations |
| `ObservableProperty<T>` | Retain needed API/member definitions | `int`, `string`, subscriptions and mutation | New Hotfix value-type T may need the **Runtime** assembly's generic definitions |
| `ObservableCollection<T>` | Retain collection/change callbacks | `int`, `string`, Add/read/subscription | Runtime definitions plus the defining BCL assemblies for List/Dictionary/comparers; not the DLL that merely contains T |
| `UIAsyncCommand` | Command callback reachability | Real async command, cancellation token, `UniTask.Yield`, disposal | Command is **not generic** here; captured delegates, async builders and state-machine generic calls still introduce AOT risks |
| UniTask / async | Retain actual awaited public paths | Witness `UniTask<int>` and sample `UniTask<BaseContext>` paths | Supplement **UniTask** definitions; unknown Hotfix state-machine value types are not proven by a Development build |
| BCL generics | Retain used collection/delegate/reflection members | Concrete collection/notification paths above | `List<T>.Add` is defined in **mscorlib** in this Unity profile. Supplement that defining assembly, not Hotfix.Sample merely because T is defined there |

Run `HotfixAotWitness.ExerciseAsync(token)` in the base-player composition to
execute the concrete combinations; its result must be **8**. Those explicit
calls are useful AOT pre-instantiation, not a universal generic-sharing guarantee.
Additional application value types, generic methods and optimized async state
machines require a new target/base-player validation.

According to the [official HybridCLR AOT generic guide](https://www.hybridclr.cn/docs/basic/aotgeneric),
future integration must:

1. Configure the sample asmdef under `hotUpdateAssemblyDefinitions` in the
   **installed** HybridCLR settings. The exported name/path/GUID draft is not
   itself an installable Settings asset.
2. Run the official target-specific generation/build workflow and inspect
   `AOTGenericReferences.cs` / `PatchedAOTAssemblyList`. The local draft's
   Runtime/UniTask/mscorlib candidates are a starting inventory, not generated
   or exhaustive metadata.
3. Archive the actual matching base-player post-strip AOT DLLs with
   base-player version, Unity version, BuildTarget and hashes. Do not mix targets
   or substitute the latest AOT DLLs on every code update.
4. Supplement **generic method-definition owners** before first use through
   the future HybridCLR adapter, check every API return code, and measure its
   non-reclaimable memory/startup cost. This stage does not call that API,
   generate metadata, install HybridCLR, or publish a hot-update DLL.

## Runnable tools and CI

### Sample run ownership

`HotfixBoundarySample.Start` consumes `RunAsync` exactly once with `AsTask()`.
Its error observer uses an independent `AsUniTask()` wrapper; `OnDestroy` awaits
the shared Task before UI shutdown and owned view-template disposal. This also
handles destruction while startup is still pending. UniTask `Preserve()` caches
a completed result, but the installed package's pending `MemoizeSource` still
forwards continuations to a single-consumer source: it is not a shared pending
Task and must not be both forgotten and concurrently awaited.

`Stage15HotfixSampleLifecycleTests` uses real Unity Start callbacks and a
later-execution-order probe that destroys the host immediately in the same Start
phase. It asserts the run is genuinely pending, checks shutdown ordering, drains
the run and verifies root/EventSystem/template destruction and zero producer or
Unobserved errors. The companion case lets the same real sample finish, checks
its reopened page and witness, then verifies normal destruction. Private-field
inspection is test-only; no new runtime diagnostic API or startup state machine
was added. See [Testing.md](Testing.md) for preserved failing-before/passing-after
XML and the refreshed full build/regression evidence.

### Menus and batch entry points

- **Tools → YUIFramework → Stage 15 → Create missing boundary sample scene**:
  saves a new standalone sample scene, never overwrites an existing scene.
  Press Play: the AOT host resolves the compiled-in mock entry, opens/closes,
  clears its idle pool, performs reflected registered prewarm, reopens and
  runs the witness. The green/blue button increments through the async command;
  root scaling/focus remain owned by UIManager. The existing login scene is not
  edited. Stop Play to drain UI and destroy the owned mock view.
- **Export boundary and metadata draft**: validates actual Player assembly
  direction and exports name/path/GUID, actual defining-assembly names and
  explicit risk/status. Existing reviewed output is never silently overwritten.

Example CI commands (Windows paths, outputs outside Assets):

```powershell
& $unity -batchmode -nographics -quit -projectPath $copy `
  -executeMethod YUIFramework.Editor.HotfixBoundaryTools.ExportDraft `
  -yuiBoundaryReport "$evidence\hotfix-draft.json" -logFile "$evidence\draft.log"

& $unity -batchmode -nographics -quit -projectPath $copy `
  -executeMethod YUIFramework.Editor.HotfixBoundaryTools.BuildStrippingProbe `
  -yuiBackend IL2CPP -yuiPlayerOutput "$evidence\Probe.exe" `
  -yuiBuildResult "$evidence\il2cpp-build.json" -logFile "$evidence\il2cpp.log"

# Optional already-installed Android ARM64 toolchain, not device certification:
# add -buildTarget Android -yuiTarget Android and use an .apk output.
```

The batch probe creates/deletes only its unique temporary scene, uses the same
stage-14 **MinimalDemo** gate with exact audited Resources, selects High stripping,
and restores backend/stripping/architecture settings in finally. A full serialized
PlayerSettings snapshot also removes implicitly materialized default-platform
entries; the original file bytes restore the user's line endings. Final post-build
hash checks run without a resync. It is an
isolated Development probe, never a Production policy pass. `-yuiBackend Mono`
selects the distinct Mono fallback measurement; it is not IL2CPP evidence.
Existing output files are rejected. A built desktop Player accepts
`-batchmode -nographics -yuiHotfixSmoke <json-path>` and exits after reflected
entry, both manager construction paths, witness and shutdown; its JSON/log
must be checked, not inferred from a build result.

`HotfixBoundaryTools.InspectStripped` accepts `-yuiStrippedDirectory` pointing to
the actual `Library\Bee\artifacts\<target>\ManagedStripped` output and a new
`-yuiBoundaryReport`. It uses reflection-only inspection to check sample entry/
page constructors, public entry, both private manager construction paths and
the witness, recording DLL hashes. This does not execute native code and is not
a substitute for the target build or a device run.
