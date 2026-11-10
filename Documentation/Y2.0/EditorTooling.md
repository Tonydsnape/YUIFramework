# Stage 14: Editor authoring and build gates

These are Editor-only tools over the existing Config, localization, presentation
and YooAsset 3 implementations. There is no second runtime registry, resource
backend, preview core or telemetry SDK. Unity 2022.3.62f2 is the verified version.
Runtime gains only the opt-in `UIRequiredReferenceAttribute`; it does not scan
objects or run a build checker in a Player.

## Entry points

Open **Tools > YUIFramework > Editor tooling** (also linked from the existing
Bootstrap window). Its three tabs provide checks/graph, page generation and
configured previews/bundles. Create a profile with **Create > YUIFramework >
Build and validation profile**. Profile changes use the ordinary Inspector;
validation does not rewrite it.

**Tools > YUIFramework > Create audited minimal-demo profile** is an explicit
compatibility choice, not a production certificate. It records enabled scenes
and the exact current Resources paths. Add the intended pages/extras and select
**Use this profile for Player build gate**. The selected asset path is stored in
project-scoped EditorPrefs, not a second serialized settings database.
CI passes `-yuiProfile` instead of depending on a developer's preference.

## Generate a page without runtime Transform.Find

The generator requires the existing Config Node dependencies; use the documented
Config setup if they are missing. Do not edit the workbook concurrently.

1. Select a saved source prefab, C# page name/namespace, empty standalone output
   folder, Config profile/ID and YooAsset package/address. Choose an output outside
   an existing asmdef/asmref root and outside Editor folders.
2. **Generate code and propose/commit workbook row** writes generated Context,
   ViewModel, UIView, explicit typed registration, a small asmdef and an ownership
   manifest. It creates a hand-owned `InventoryContext.cs` only if absent.
   The existing `UISettings.xlsx` receives an additive `(Profile, Id)` row using
   the existing schema/defaults; existing designer values are not overwritten.
3. **Run existing Config client exporter** publishes the existing three-table
   JSON/MessagePack/typed outputs through its existing six-directory transaction.
   Wait for Unity compilation before binding.
4. **Bind compiled view to new generated prefab** creates `Inventory.prefab` in
   the owned output folder. It works in a temporary prefab scene, serializes
   actual control references and closes the scene. The source prefab is unchanged.
5. Collect that new prefab at the declared package/address. Add its explicit page
   declaration to the build profile, using
   `typeof(Game.UI.InventoryContext).AssemblyQualifiedName`. At the application
   composition root pass `InventoryRegistration.Map(snapshot)` to the existing
   `UIManager.RegisterBatch`, then open/navigate normally.

`InventoryContext.Controls.Control0` etc. are typed cached references.
`HandleInit` installs the generated ViewModel and calls `OnGeneratedInit`;
business logic belongs in the hand-owned partial, not `.g.cs`.
The generated assembly explicitly references the existing Examples assembly to
reuse `SampleUIConfiguration.Convert`; this stage does not move that shared
converter or establish a new Hotfix assembly boundary.

Supported automatic controls are built-in uGUI Selectables/Images and TMP text.
Custom control subclasses require explicit hand-owned bindings. Duplicate
sibling names are safe for generated references: the manifest stores sibling
index routes, never runtime name searches. Its source GUID/path/hash must still
match at bind time; changed or reordered source content requires regeneration.
A hand-typed source UIView, or serialized users of a plain UIView that would
be replaced, is rejected rather than silently breaking references.

ASCII identifiers/keywords, namespace, path escapes, assembly ancestry, compiled
Context/View/ViewModel collisions and existing output ownership are checked.
Existing generated assembly-name conflicts are refused even without a matching
Context type. Output ancestors containing symlinks/junctions are rejected for
generation, reports, bundle output and isolated workspaces.
Generated files have hashes; hand edits to them cause refusal. Repeating
generation preserves the hand partial and identical designer row. Identity
changes require an explicit migration. Binding never overwrites an existing
output prefab; delete/rebind explicitly only when its edits may be discarded.

Code/manifest/hand-file/workbook writes roll back on exceptions, including every
one of the eight first-generation write boundaries. A workbook changed during
proposal is refused before publication. This is process-level exception
rollback, not crash recovery or concurrent Excel/editor isolation. Restoration
continues after an individual rollback path fails and aggregates
the primary/rollback errors rather than claiming success.
Generation and the subsequent Config export are separate explicit transactions, not one
distributed transaction.

For CI, `UIPageGenerator.GenerateBatch` reads `-yuiRequest <request.json>` with
Name/Namespace/OutputFolder/Profile/Id/Address/Package and `-yuiPrefab <asset path>`.
Start a new Unity process after generation/export so generated types are compiled;
`BindCompiledView(manifestPath)` is the public binding entry.

## Explicit profile and coverage

| Profile data | Meaning |
|---|---|
| `Scenes`, `Extras`, `Pages[].Prefab` | Declared roots; only their actual transitive AssetDatabase dependency closure is inspected |
| `Pages` | Explicit Config profile/ID/assembly-qualified Context/prefab map; all rows in each selected Config profile must be mapped |
| `Navigation` | Declared From/To/Action edges; endpoints must exist; arbitrary business Navigate calls are not statically inferred |
| `Collector`, `Packages`, `DefaultPackage` | Real saved YooAsset collector and packages whose main-asset addresses are checked |
| `RequiredResources` | Explicit package/address/optional expected asset; adds actual resources to the scan closure |
| Config JSON paths | Authoring snapshots used by the existing typed parser/catalogs, not ad-hoc string searches |
| Production Config package/prefix/suffix/format | Actual packaged TextAssets must resolve and decode equal to the inspected JSON snapshots |
| `MaximumAtlasSize`, `CheckTextContent`, `FailOnWarnings` | Explicit atlas/content policy and optional warning escalation |
| `AuditedDemoResourcePaths`, `DemoCodePageIds` | Exact declared demo-only exceptions; never exempt Resources or code-built pages under Production |

Reports contain UTC, policy/target, `Complete`, `ProductionCertified`, coverage,
skips, issues and graph edges. Errors or incomplete coverage block a build.
MinimalDemo always has `ProductionCertified=false`, even without errors.
Missing/ambiguous collector, invalid rules or failed scans are explicit errors,
not empty-success fallbacks. Production requires exactly one saved collector
matching the official builder's source.

All non-Editor `Assets/**/Resources` assets are implicit Unity Player inputs and
are audited independently of the dependency closure. They cannot be hidden by
omitting them from a scene. MinimalDemo requires exact audited paths; Production
rejects them. The existing Resources login cover, game bootstrap and framework
demo assets are intentionally not moved, rewritten or relabeled production.

## Checks and localization

| Check | Default result / boundary |
|---|---|
| MissingScript, unresolved serialized object, `[UIRequiredReference]` null | Error, asset + object sibling route + property; ordinary nullable fields are legal |
| Nested sorting Canvas / missing camera on nested non-overlay Canvas | Error; root cameras may be assigned by the existing runtime |
| Decorative RaycastTarget, duplicate sibling name | Warning requiring intent review, not automatic mutation |
| Mask without Graphic | Error |
| Multiple stencil/rect masks, LayoutGroup + ContentSizeFitter | Warning; heuristic risk, not proof of a layout cycle or frame budget |
| Config/page/context mismatch, missing/wrong-package/wrong-type address | Error using actual `BeginCollect(package, false, false)` data |
| Invalid/duplicate collector address/rule | Error and incomplete collection, including official producer exception chain |
| Localization key/glyph, missing font atlas/dynamic source/shader | Error via existing content checks and actual assets |
| Font atlas or SpriteAtlas configured size budget | Error; inspect real atlas textures/packables/settings, never repack source |
| Text overflow | Warning based on existing transient content measurement |

Required-null detection applies to directly annotated serialized fields, including
the generated control fields; it does not infer a project-wide nullable schema.
Unresolved references are inspected throughout Unity's serialized properties.
Font texture-size checks are not a total CPU/GPU memory budget; SpriteAtlas
settings are not a newly packed device atlas measurement. Runtime frame
performance still uses [Diagnostics.md](Diagnostics.md), not a static lint score.

Click an issue to open its prefab/scene and select the actual GameObject by
sibling index (including duplicate names); asset-only issues ping the asset.
Click graph nodes to locate assets/pages. The graph displays explicit navigation,
direct root dependency edges and actual collector asset/bundle mappings. Coverage
includes the transitive inspected closure. At most 100 filtered edges are drawn;
exported JSON retains all recorded edges. Dynamic code-only dependencies must be
declared explicitly; unrelated vendor/demo prefabs are not globally scanned.

Serialized scenes are opened in isolated preview scenes and closed in finally.
Unity 2022.3's exact internal preview loader is used because no public serialized
preview-open API exists; unavailable API or scan failure is Incomplete, never a
fallback that opens/saves the user's live scene.

## Actual configured device preview

From **Preview / bundles**, select a declared page. The existing device preview
now accepts real locale, theme, font scale, six portrait/landscape/tablet presets
and L/T/R/B pixel safe insets. It uses existing `UIScreenLayout`/
`UISafeAreaContent`, real Config catalogs/bindings and real font/Sprite/Material
objects resolved through the selected collector or explicit demo resources.
Unknown locales/themes/resources fail visibly instead of displaying invented data.
Address lookup honors the real collector's per-package `LocationToLower` option;
package names remain case-sensitive.

No Play mode, second EventSystem, input lock or runtime service singleton is
created. Nonvisual/business/custom UI scripts and EventSystems/input modules are
removed from the inactive clone before UI activation, not merely disabled
(disabled components can still receive Awake). Supported visuals are built-in
uGUI/TMP plus Config text bindings; dependent components that prevent safe removal
are refused. This is not a sandbox for arbitrary Unity import/OnValidate callbacks.
Font/fallback/atlas/material graphs are deep-cloned; borrowed global
TMP settings are scoped and restored, including errors. Close/rebuild disposes
bindings, services, clones and preview scenes without saving source assets.
Cleanup continues through individual release errors and then aggregates failures.
The isolated preview camera is Game-type: uGUI excludes Preview-type cameras
even when their Canvas meshes exist. Actual D3D11 pixel rendering, not only
RectTransform math, is covered by the separate graphics test.

This is a fixed Editor content/layout preview, not touch input, OS keyboard,
screen-reader, device rasterization, FPS, IL2CPP or mobile GPU certification.
Null graphics can run geometry/content checks but actual rendering is Unavailable.

## Player build gate and production workspace

`UIBuildGate : IPreprocessBuildWithReport` invokes the same validator and throws
`BuildFailedException` before building invalid content. No selected profile fails
closed. Reports default to `Library/YUIFramework/last-validation.json`, not Assets.
Use **Use this profile for Player build gate** for explicit MinimalDemo builds.

Production callers must use:

```csharp
BuildReport report = UIBuildGate.BuildPlayer(options, productionProfile);
```

The wrapper audits the actual `BuildPlayerOptions.scenes`, not merely current
Build Settings. The preprocess hook runs again; a direct Production build that
bypasses the wrapper is refused because its arbitrary scene list is unknowable.
Profile/actual target mismatches and unscanned scenes also block.

When the checkout includes the intentional demo Resources, **Prepare isolated
production workspace** exports Assets/Packages/ProjectSettings into a new external
directory, excluding only explicitly audited demo files/metas. It refuses to
exclude declared target dependencies, never moves source assets, does not overwrite
an existing workspace and records per-file SHA256 plus exclusions.
Short Windows paths are required where Unity's 260-character limit applies.
The manifest says **PreparedNotValidated**. Configure actual production scenes,
collector, packaged Config/presentation assets and rerun the same gate in that
workspace. Unregistered dynamic code loads remain the application's declaration
responsibility. Export is neither a bypass nor a Production-passed certificate;
it deliberately does not copy Library or the Node authoring toolchain.

Example CI in a prepared isolated project:

```powershell
& $unity -batchmode -nographics -quit -projectPath D:\BuildWork\Game `
  -executeMethod YUIFramework.Editor.UIBuildGate.ValidateBatch `
  -yuiProfile Assets/Settings/ProductionUI.asset `
  -yuiReport D:\BuildEvidence\ui-validation.json -logFile D:\BuildEvidence\validate.log
if ($LASTEXITCODE -ne 0) { throw "UI validation failed" }

& $unity -batchmode -nographics -quit -projectPath D:\BuildWork\Game `
  -executeMethod YUIFramework.Editor.UIBuildGate.BuildPlayerBatch `
  -yuiProfile Assets/Settings/ProductionUI.asset `
  -yuiPlayerOutput D:\BuildOutput\Game.exe -logFile D:\BuildEvidence\player.log
```

Validation/build exceptions produce a nonzero batch exit. A validation report is
required evidence; absence is not a pass. These tools do not upload output.

## Real YooAsset build and version history

Set profile `Target`, `BuildPackage`, `PackageVersion`,
`Pipeline=LegacyBuildPipeline` and an absolute external `OutputRoot`.
Select the exact official active collector asset and validated package. The tool
checks the installed target module and current active target; switch the isolated
project explicitly if needed. No implicit target switching, StreamingAssets
copying, Addressables or extra build SDK is introduced.

**Validate and build real local AssetBundles** invokes the installed official
YooAsset 3 Legacy pipeline with result verification and LZ4. It records official
output directory/report/version, every output file SHA256, UTC, status and full
failure chain in appended `ui-build-history.jsonl`. Existing version directories
are refused. The window and record expose the actual official `ReportSummary`
(asset/bundle count, bundle bytes, builder seconds), checked against the requested
target/package/version/pipeline. A missing report on canceled/failed builds is
explicitly Unavailable, never a zero-valued summary.
Hand-edited history is not overwritten. Open that history/reports
through the window. Failed validation returns a visible Failed record; failures
before a valid external output is established are not written to an unsafe path.

`UIYooAssetBuilder.Build(profile, token)` supports `CanceledBeforeBuild`.
The official builder is synchronous and has no safe mid-task cancellation API;
late cancellation is explicitly `SucceededCancellationTooLate`, not fake rollback.
The window provides a confirmation cancel before starting.
`UIBuildGate.BuildBundlesBatch` is the equivalent CI entry, with `-yuiProfile`.

Acceptance builds a real StandaloneWindows64 bundle, loads its TextAsset back,
reads official report/version and verifies history preservation. Module capability
is reported separately; it does not claim a mobile Player release, CDN publication or runtime update
deployment. Exact evidence, requirements matrix and limitations:
[Testing.md](Testing.md). Stage 15/HybridCLR is not part of this stage.
