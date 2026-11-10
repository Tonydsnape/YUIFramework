# UPM quick start (Unity 2022.3)

This repository keeps `Assets/YUIFramework` as the **only maintained implementation**.
`Distribution/package.json` describes the private `2.0.0-rc.1` package. Exported
packages are reproducible build outputs, not a second source tree. No registry
publication, license grant or mobile certification is implied.

## Export from the authoring repository

Use **YUIFramework > Package > Export private RC package**, selecting an external
parent. The destination must be new, outside/not above the project, and without
junctions/symlinks. Existing output is never overwritten. Batch equivalent:

```powershell
& $Unity -batchmode -nographics -projectPath $AuthoringProject `
  -executeMethod YUIFramework.Editor.UIPackageExporter.ExportBatch `
  -yuiPackageOutput D:\Packages\com.tonydsnape.yuiframework -quit -logFile $ExportLog
```

`VerifyBatch` accepts the same output argument. `source-manifest.json` records
every distributed relative source/output path and SHA256, including fixed metadata.
Verification rejects changed, extra, missing or linked files. Export is staged
then renamed; failure removes only its owned staging directory. Two exports from
the same inputs must have identical bytes. Unity import must not add metadata or
rewrite the package; verify again after consumption.

Layout: `Runtime` (including Configuration/Localization/generated schemas and
separate Bootstrap/CodeLoading/legacy assemblies), `Editor`, `Tests`,
`Samples~/Complete`, `Documentation~`, `Tools~/Config`, `ThirdParty`.
Every Editor assembly explicitly limits `includePlatforms` to `Editor`.
Runtime does not reference Editor, Examples, Hotfix business or HybridCLR.
The pure generated Config assembly is separate from application examples.

Commercial SuperScrollView/local integration, the application's dynamic font
asset, login artwork, node_modules, caches, downloaded tool outputs and validation
projects/Players/reports are not exported. Third-party notices stay alongside
their files. `UNLICENSED` records unresolved author-code distribution rights;
do not upload the private package to a public registry.

## Install in a new project

Do **not** install the package beside the original compiling Assets framework.
Use a new Unity 2022.3.62f2 project. Package dependencies use semver, but Git
dependencies and scoped registries belong to the **consumer** manifest:

```json
{
  "dependencies": {
    "com.tonydsnape.yuiframework": "file:D:/Packages/com.tonydsnape.yuiframework",
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#ceac8d6946b1125fe782cd171fbcb245b567dbf9",
    "com.unity.test-framework": "1.1.33"
  },
  "scopedRegistries": [
    { "name": "OpenUPM", "url": "https://package.openupm.com",
      "scopes": ["com.tuyoogame.yooasset"] }
  ],
  "testables": ["com.tonydsnape.yuiframework"]
}
```

Merge these fields with the new project's existing Unity module dependencies;
do not discard its manifest. The package pins YooAsset 3.0.5, UniTask 2.5.11,
TMP 3.0.7, uGUI 1.0.0 and Newtonsoft 3.2.1. Keep the resolved lockfile.
`testables` is required for these **nonembedded** package tests. Before importing
any sample, the package must compile and its public boundary/lifecycle tests run.

## Actually import and run

In Package Manager select YUIFramework, expand Samples, and **Import Complete UI
flow**. Unity's `Samples~` does not compile before Import. The normal destination
is `Assets/Samples/YUIFramework/2.0.0-rc.1/Complete UI flow`.
Do not manually move the example asmdefs into the core Runtime directory.

Use **YUIFramework > Release candidate > Create new demo workspace** in this
clean consumer. It requires saved scenes, no existing collector and no previous
`Assets/YUIReleaseCandidate`; existing user work is never overwritten. It creates:

- a real saved YooAsset collector with `YUIReleaseCandidate/RcMarker`;
- `CompleteUI.unity` with the actual `ReleaseCandidateSample` component;
- a locatable, audited **MinimalDemo**, not Production, build profile.

Open `Assets/YUIReleaseCandidate/CompleteUI.unity` and Play. EditorSimulate goes
through the existing resource Bootstrap and asynchronously leases the actual
marker before UI entry. Buttons exercise navigation/Back, Popup/modal focus,
MVVM commands, a bounded observable list, English/French labels and a real active
scene switch that drains scene UI while retaining global navigation. The list
has at most 12 rows and is **not** a virtual-list/0-B-per-frame benchmark.

In Player, this sample explicitly uses Offline bootstrap and built-in YooAsset
bundles. Its code-built views and Config Resources are declared demo inputs.
Bootstrap uses this demo's explicit `com.yuiframework.rc.stage16` identity on
every platform; standalone Unity does not always supply Application.identifier.
This proves resource readiness/manifest loading, not CDN downloads. Use the real
Host profile/backend/policies in [Bootstrap.md](Bootstrap.md) for application
updates; no mock network success or second startup state machine is introduced.

The imported sample includes the historical Hello/Y2/system/diagnostic/Hotfix
examples. For the old Hello presentation sample, explicitly run **YUIFramework >
Stage 11 > Create missing sample assets** in the consumer first. This creates
application-owned fonts/settings/images/materials there from the licensed package
sources; it never copies the author's dynamic font asset. Existing assets are
preserved. TMP preview/content checks require application TMP settings when TMP
text is present; a code-only scene without TMP does not require them.

The default sample Config resolver accepts the authoring path or exactly one
imported ConfigData root. Missing/ambiguous imports fail explicitly. Applications
with different layouts should inject an explicit source/path, not rely on a
project-wide search or production Resources fallback.

## Application authoring and upgrades

Use **Copy Config authoring tools into project** once; it refuses an existing
`Config` directory. Run `npm ci` there if dependencies are missing, then validate.
The existing page wizard still writes only owned generated files, hand partials
once, and an additive Excel row. Its emitted assembly references the pure typed
Config assembly, not Examples. In a consumer do not regenerate schema classes
into Assets beside the package's identical classes:

```powershell
Set-Location Config
node src\cli.js --validate
node src\cli.js --target client --no-unity-sync
```

This writes `Config/Client/{json,bytes,generated}`. For the unchanged shipped
schema, compare `generated/ConfigBindings.g.cs` with the package schema, then
publish JSON/bytes to the application's explicitly configured Config source (or
the imported demo ConfigData/Resources paths). Register each generated
`Registration.Map(snapshot)` explicitly in one batch. Never edit installed
package code. A changed schema/new tables requires an application-owned schema
assembly/catalog/namespace, or regeneration and re-export from the authoring
repository; it is not a silent duplicate-type fallback. Production uses
`ResourceConfigSource` with the actual YooAsset address/package map.

For an upgrade, export to a new directory, compare manifests and release notes,
change the consumer file dependency, resolve, then rerun the gates. Reimport
samples only after backing up hand changes; the exporter never edits imported
application copies. Follow [Migration.md](Migration.md) for Y1 projects.

## Build and smoke entry points

Switch the **isolated consumer** to Android/iOS before building. Player/report
outputs must be new absolute paths outside/not above the project, without links.
They must not overlap each other or the external bundle root. Validation happens
before bundles, StreamingAssets or PlayerSettings changes; report parents are
created only after validation, and reports use create-new rather than overwrite.
The bundle root may already exist under the existing append-only version/history
policy; a version is never replaced. Reports remain local, not uploaded.

```powershell
& $Unity -batchmode -nographics -projectPath $Consumer `
  -executeMethod YUIFramework.Editor.ReleaseCandidateTools.MobilePreflight `
  -yuiMobileTarget Android -yuiRcReport D:\Evidence\android-preflight.json -quit

& $Unity -batchmode -nographics -projectPath $Consumer -buildTarget Android `
  -executeMethod YUIFramework.Editor.ReleaseCandidateTools.BuildDevelopmentSmoke `
  -yuiBundleOutput D:\Evidence\bundles `
  -yuiPlayerOutput D:\Evidence\YUI-RC.apk `
  -yuiRcReport D:\Evidence\android-build.json -quit
```

The latter runs the real official YooAsset builder, retains its report/history,
requests `OnlyCopyAll` into a new version-owned external builtin directory
(including the official **BuiltinCatalog.bytes**, not just patch files), then
copies those built-in files into the consumer's StreamingAssets,
uses the same UI build gate, and builds Development/IL2CPP/High (Android ARM64).
It restores original scene/profile/PlayerSettings bytes. Created built-in bundles
are intentional consumer outputs; a subsequent build refuses to overwrite them.
Use a fresh consumer or explicitly relocate that exact owned bundle directory.
Windows Mono can be selected with `-buildTarget Win64 -yuiBackend Mono`.

The automated Player runs the real sample smoke, captures the rendered UI, drains
ownership, writes a temporary-cache `yui-rc-smoke.json` and logs
`[YUI-RC]`. It **exits after reporting, on success or failure**; an exit alone is
not a pass. On Android it explicitly uses the activity's `getCacheDir()` through
Unity AndroidJNI (Unity's temporaryCachePath can select external storage).
A Development build permits
`adb exec-out run-as com.yuiframework.rc.stage16 cat cache/yui-rc-smoke.json`
and the sibling `cache/yui-rc-ui.png`, without storage grants or root. Validate
the JSON fields/PNG contents, not merely adb's exit code. Temporary cache is
ephemeral; collect it promptly. The package
ID is the dedicated `com.yuiframework.rc.stage16`; check the target device before
installing and never overwrite an unrelated installation. Its display name is
**YUI RC Stage16 Smoke**, not an update to another application. A successful build
does **not** prove installation/launch/device UI. Read the actual report and
observe the Player independently. Automated button events are not manual
touch/accessibility/rotation or performance certification.

iOS preflight reports host/module blockers. Native build/signing/device execution
requires macOS/Xcode and an authorized iOS device. Windows/Android evidence cannot
replace those gates. See [ReleaseCandidate.md](ReleaseCandidate.md).

## Linker and optional integrations

Unity 2022.3 does not honor a package-internal `link.xml` as an application's
Assets linker root. Core `UIYooAssetLinker` implements `IUnityLinkerProcessor`
and supplies `Library/YUIFramework/Linker/YooAsset.link.xml` to the actual linker,
even without sample Import. It preserves only the default constructors for
YooAsset 3.0.5 `BuiltinFileSystem` (Offline/Host) and `SandboxFileSystem` (Host),
which YooAsset creates with Activator. Custom file systems need application-owned
rules. This is constructor retention, not Host/CDN execution certification.
The optional Hotfix sample's real Import places its
`Hotfix.Sample/link.xml` beneath **Assets**. Keep it with the imported sample or
create application-owned rules before removing the sample. Use the existing
`HotfixBoundaryTools.BuildStrippingProbe` after Import to build/inspect High
stripping and the two reflected Context paths. Preserve, concrete AOT generic
witnesses and future HybridCLR metadata are distinct guarantees; stock IL2CPP
does not execute downloaded Hotfix DLLs. No HybridCLR dependency is installed.

The commercial list integration is intentionally not in this private package.
Install licensed vendor code and the existing optional adapter separately in the
application; do not copy it into the distributable package. The complete sample
does not require the vendor.

Official Unity 2022.3 references:
[layout](https://docs.unity3d.com/2022.3/Documentation/Manual/cus-layout.html),
[samples](https://docs.unity3d.com/2022.3/Documentation/Manual/cus-samples.html),
[tests](https://docs.unity3d.com/2022.3/Documentation/Manual/cus-tests.html),
[stripping](https://docs.unity3d.com/2022.3/Documentation/Manual/ManagedCodeStripping.html).
