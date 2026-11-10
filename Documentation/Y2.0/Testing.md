# Y2.0 Testing Baseline

## Stage 16 local implementation evidence; mobile acceptance blocked

These are actual Unity **2022.3.62f2** results on **2026-11-04 UTC**, not promises
or replacements for device gates. Source HEAD is
`26e405da8e46b3ac73b24389fc4cad14bb478c76` plus the uncommitted stage-16
snapshot. The review index is `stage16-acceptance-local-final.json` in the session
evidence directory. No commit, staging, push or package publication was performed.

### Complete isolated and clean-consumer runs

| Gate / actual XML | Passed / total | UTC interval | Seconds |
|---|---:|---|---:|
| `stage16-final10-installed-EditMode.xml` | 270/275 | 09:52:35-09:53:21 | 45.6923964 |
| `stage16-final10-installed-PlayMode.xml` | 223/223 | 09:53:35-09:54:05 | 30.3189733 |
| `stage16-final10-no-vendor-EditMode.xml` | 270/275 | 09:52:38-09:53:22 | 43.3428697 |
| `stage16-final10-no-vendor-PlayMode.xml` | 208/208 | 09:53:37-09:54:04 | 27.031109 |
| `stage16-final9-fresh-base-EditMode.xml` | 5/5 | 09:49:45-09:49:45 | .0849458 |
| `stage16-final9-fresh-base-PlayMode.xml` | 1/1 | 09:49:55-09:49:55 | .1279844 |
| `stage16-final9-fresh-imported-EditMode.xml` | 5/5 | 09:52:56-09:52:56 | .1042881 |
| `stage16-final9-fresh-imported-PlayMode.xml` | 5/5 | 09:53:07-09:53:13 | 5.6091415 |

The five full-suite Edit skips are the existing stage-14 Explicit producers, not
silently waived failures. All five were additionally executed against this source
in their required isolated/renderer processes:

| Actual XML | Coverage | Passed | UTC / seconds |
|---|---|---:|---|
| `stage16-final-editor-explicit-EditMode.xml` | Negative real Player preprocess; generator rollback/idempotence/hand code; official bundle/report/version/history | 3 | 10:14:27-10:14:50 / 22.3989549 |
| `stage16-final-renderer-EditMode.xml` | Non-Null real Editor camera, safe-area pixels and French text | 1 | 10:15:56 / .5470543 |
| `stage16-final-authoring-compiled-EditMode.xml` | Fresh generated C# assembly, four serialized duplicate-name controls, ViewModel, exported Config row, typed batch map, real Open/Close/release | 1 | 10:20:49 / .5249583 |

The authoring producer runs the unchanged Config exporter only in its derived
project; it does not edit the author's Excel/11 registrations or install new
dependencies. Original stage-9/10 local GC assertions remain unchanged; neither
these nor the complete-flow list certify full-frame 0-B, FPS or mobile budgets.

`Stage16PackagingTests` covers reproducible exports/tampering, no-TMP fresh
content, relative/internal/ancestor/existing/overlapping output rejection,
Windows junctions, unchanged marker/report/settings/StreamingAssets on rejection,
create-new report parents and rejection of missing builtin catalog. Package-only
tests additionally prove Editor platform/API boundaries and actual linker callback
rules **before sample Import**. Eighteen focused cases passed in
`stage16-catalog-directed-EditMode.xml` (09:33:46-09:34:12, 26.3329562 s).

The new consumer starts with **zero copied business Assets**, a nonembedded file
dependency and manifest.testables. Actual Unity `Sample.Import` creates compiling
Examples/Hotfix assemblies and an Assets linker root. Imported Play tests exercise
real Bootstrap/marker, immediate destruction while actual Start is pending,
migrated visible Hello presentation, complete flow and package lifecycle.

### Actual native builds and independent desktop execution

| Report | Target / backend / stripping | Actual UTC interval | Build seconds / total build bytes |
|---|---|---|---|
| `stage16-final9-native-reports/android-build.json` | Android ARM64 / IL2CPP / High / Development | 09:55:47.4498007-09:57:48.3893967 | 111.4463326 / 394,390,553 |
| `stage16-final9-native-reports/mono-rc-build.json` | Windows64 / Mono / High / Development | 09:47:18.9236731-09:47:46.8271367 | 19.1393135 / 107,912,701 |
| `stage16-final9-mono-high.json` | Windows64 Hotfix boundary / Mono / High / Development | 09:55:28.8899261-09:55:57.1143551 | 27.7703449 / 107,910,067 |

All three BuildReports have zero errors. Android APK size is **35,717,986 bytes**,
not total build bytes. Its actual zip inventory includes ARM64 libil2cpp,
global-metadata and **BuiltinCatalog.bytes**. Official `OnlyCopyAll`/catalog tasks
produce the builtin set in a new external version directory; merely copying patch
output was insufficient. Evidence retains the official report, hashes and history.

The core UnityLinker callback supplies two precise default-constructor rules for
YooAsset Builtin/Sandbox file systems, independently of sample import.
`stage16-final9-yoo-stripped.json` inspects both in the actual Android stripped
YooAsset.dll. `stage16-final9-{android-hotfix,mono}-stripped.json` separately
inspects the retained Context ctors, generic Open and Type-prewarm paths.
This is not unknown generic/native execution or HybridCLR certification.

Actual desktop processes both exit **0**:

- `stage16-final9-mono-rc-execution.json`, 09:48:21.528961-09:48:26.830909:
  real Offline package initialization/marker, **nine** navigation/modal/MVVM/list/
  locale/scene checks, UI shutdown, restored scene and destroyed templates.
  Null graphics explicitly reports screenshot **Unavailable**.
- `stage16-final9-mono-execution.json`, 10:02:06.894350-10:02:10.511280:
  reflected entry, generic Open, Type-prewarm, concrete generic witness **8**,
  cleanup; no downloaded DLL execution on stock IL2CPP is claimed.

### Android device and iOS: not passed

Device failures remain evidence, not successful runs. The first APK exposed a
High-stripped YooAsset default ctor; the next authorized device run confirmed
file-system construction but failed with a missing official builtin catalog (404).
Both root causes were fixed and the later actual APK/stripped/desktop Offline
evidence above reflects those fixes.

`stage16-final9-device-evidence.json` records the latest authorized update:
installation **succeeded**, but `am start -W` timed out. App-specific logs show
onResume followed by onPause before entering Unity; the activity is stopped and
there is **no valid current-run smoke JSON or screenshot**. No functional device
pass or rendered Android UI is claimed. Further install/launch/foreground/device
changes were stopped; unattended coordination cannot certify a user's foreground/
bright-screen action. The dedicated test application remains installed.

`stage16-final9-native-reports/ios-preflight.json` records **moduleAvailable=true**
and complete scoped MinimalDemo validation, but status **Blocked** with
`iOSNativeHostUnavailable`: this Windows host has no macOS/Xcode native signing/
build/device environment. No iOS native build or device run occurred. No new
HybridCLR, SDK/toolchain, cloud resources, metadata or DLL publication was added.
**Stage 16's original mobile gates and full RC acceptance remain blocked.**

### Identity, package/document correspondence and diagnostic boundaries

Full Assets/Packages/ProjectSettings maps have **795** installed inputs and
**727** without vendor. Each final10 pre-source = pre-copy = post-source =
post-copy: zero missing/extra/mismatch and no synchronization after runs.
SHA256 of their canonical JSON maps is respectively
`2bb44469d516154c6f394d9361900b60dc726a4eb1e7ea8783c6e66266caa3b5` and
`44ae130c255fd4734621940b9a284d059d115e4e28beb67f691fcc8a415e8630`.
The 47 baseline tracked login/Config inputs checked by the final audit are
unchanged. The original dirty .gitignore/font and info/exclude remain protected.

Runtime/native/consumer evidence uses **rc9**; final documentation is exported
in **rc10**. The final index records a manifest comparison proving only documents
and their inventory differ: no runtime, Editor, sample, test, Config, package
dependency or metadata bytes differ. Final rc10 is verified after import too.
Pure documentation does not trigger a pretend rerun of native builds.
Android consumer build maps preserve all 147 pre-existing inputs and add only
13 intended builtin files/metas; no source/profile/settings file is changed.

Final installed logs have zero C# errors/warnings; no-vendor Edit has **ten
CS0618 log occurrences for five intentionally exercised compatibility members**
(duplicated by compiler reporting). Both full Edit logs contain two intentional
diagnostic negative-test exception lines, and Play logs three each. They retain
four/three Debug warning calls and one unavailable licensing access-token notice.
No continuation-registration, NRE or Unobserved failures appear. The separate
negative Player producer intentionally emits build failure diagnostics; it is
not a successful-build warning-free claim.

Raw logs remain local. `stage16-final-review-log-manifest.json` links raw hashes
to **redacted** `stage16-final-review-logs` copies, removing launcher environment
assignments/credential lines and APK installation identifiers. Review copies,
not raw Android environment dumps, are the handoff logs. Earlier no-XML selector,
metadata-normalization, missing-catalog/device and stalled SDK-list attempts are
kept separately and are not mixed into passing final runs.

## Stage 15 lifecycle revision (historical accepted baseline)

The initial `final4` evidence below did **not** cover destroying the real sample
while Start was pending. The added regression first failed against the unchanged
sample, before the runtime fix; the normal-startup companion passed. All files
and timestamps here remain local session evidence, not repository test products.
Unity 2022.3.62f2, 2026-11-03 UTC:

| Gate | Passed / total | Actual XML UTC interval | Duration (s) | XML |
|---|---:|---|---:|---|
| Before fix, real Start/Destroy | 1/2, **one expected reproduction failure** | 03:21:59-03:21:59 | .1908222 | `stage15-lifecycle-before-fix5-PlayMode.xml` |
| Same cases after Task-backed fix | 2/2 | 03:22:29-03:22:30 | .2061058 | `stage15-lifecycle-after-fix-PlayMode.xml` |
| Full installed, Edit | 252/257 | 03:31:27-03:32:09 | 42.6564526 | `stage15-lifecycle-installed-final2-EditMode.xml` |
| Full installed, Play | 221/221 | 03:32:33-03:33:05 | 32.004424 | `stage15-lifecycle-installed-final2-PlayMode.xml` |
| Full no-vendor, Edit | 252/257 | 03:31:41-03:32:16 | 35.1511658 | `stage15-lifecycle-no-vendor-final2-EditMode.xml` |
| Full no-vendor, Play | 206/206 | 03:32:33-03:33:00 | 27.3744028 | `stage15-lifecycle-no-vendor-final2-PlayMode.xml` |

Both Edit runs retain the same five historical stage-14 Explicit skips; none of
the thirteen stage-15 cases is skipped. Existing stage-9/10 GC assertions are
unchanged. No Config/Node/dependency or unrelated runtime changes were needed.

### Reproduction, ownership and cleanup

`Stage15DestroyAfterStartProbe` runs after the real sample's Start and destroys
the host immediately in the same Start phase. It does not invoke Start through
reflection, await a test substitute, sleep until completion, or postpone Destroy
to hide the race. Both before/after XML record **ActualStart=True** and
**pendingAtBoundary=True**. The pre-fix failure records
`Already continuation registered` and **shutdownBeforeRunSettled=True**.

The sample now consumes the original UniTask once via `AsTask()`. An independent
`AsUniTask()` observer and the destruction drain share that Task. Cleanup awaits
the run before shutdown/disposal, following the existing presentation-binding
pattern rather than changing the UniTask package or adding another state machine.

Both new cases run again inside both final full Play suites. Their XML records
**shutdownBeforeRunSettled=False**, **templateCreated=True**, **producerErrors=0**
and **unobserved=0**. Assertions verify the run settles, UI shuts down and owned
root/EventSystem/template become Unity-null; any returned Context is Released.
Normal startup also verifies the reopened page and real generic witness PASS.
The tests capture and assert every Error/Exception/Assert and Unobserved error;
their local LogAssert flag and subscriptions are restored in finally. This is
not a blanket acceptance of failing log messages.

### Refreshed real High-stripping builds

| Actual build | BuildReport UTC interval | Seconds | Result / total build bytes |
|---|---|---:|---|
| Android ARM64 / IL2CPP / High / Development | 03:31:11.8836505-03:32:25.766247 | 73.1290416 | Succeeded / 529,493,470 |
| Windows64 / Mono / High / Development | 03:33:21.3563481-03:33:45.0787749 | 23.2778195 | Succeeded / 117,075,676 |

Reports are `stage15-lifecycle-{android,mono}-high-final2-build.json`;
both have zero BuildReport errors. The actual APK is **42,207,156 bytes**, not
the total build size. Its SHA256 is
`770daa52e427e330ef9cd21d65a5e1ac8d8bffffb48673c85aaa6631bb6ef02e`.
The evidence hashes its actual ARM64 `libil2cpp.so` (42,126,456 bytes) and
`global-metadata.dat` (6,524,212 bytes), plus generated native code for sample
Start/Cleanup, reflected entry/constructors and Type-prewarm.
`stage15-lifecycle-{android,mono}-stripped-final2.json` inspects each build's
actual stripped DLLs, preserving both manager Activator paths and public ctors.

The rebuilt Windows Mono Player actually runs from
03:33:59.0124986-03:34:02.2367798 UTC and exits **0**. Its
`stage15-lifecycle-mono-high-final2-smoke.json` verifies reflected entry, generic
Open, registered Type-prewarm, witness **8** and UI shutdown. Android native
compilation/APK inspection is **not** native runtime execution or a device pass.
The earlier Windows IL2CPP C++/SDK limitation, no HybridCLR/metadata/publication
boundary and unvalidated Android/iOS device coverage remain unchanged.

### Refreshed identity, logs and limits

`stage15-lifecycle-acceptance-evidence-final2.json` records XML/build/output/log
hashes, actual times, observations and Git boundaries. Its complete
Assets/Packages/ProjectSettings maps contain **766 files** for installed/Android
and **698** without vendor. Each has pre-source = pre-copy = post-source =
post-copy: **zero missing/extra/mismatch**, without a post-run resync.
The `stage15-lifecycle-*-final2-{pre,post}-{source,copy}-sha256.json` hashes are:

- installed/Android: `a894e6ec2376dcdbbd7d6c45432ab554c92b1e4b06bf459bc2bb3dc351d187cf`;
- no-vendor: `1ded7f78b65e9a0679cc1424027c72248c32e873e8bb4b221de905350ab7cb19`.

The first refreshed full suites/builds passed but their identity audit failed:
Unity expanded two new minimal test metas with MonoImporter data. That failed
audit is retained as `stage15-lifecycle-first-manifest-drift.json`. Only those
owned new metas were normalized with their original GUIDs; all final2 suites,
builds, inspections and Player execution were rerun with fresh pre-maps. The
older compiler/fixture attempts and initial final4 index remain intact.
Compared with final4, input changes are only sample Task backing, the Play test
asmdef reference and two new test files with metas; no removals.

After-fix logs have zero C# warnings/errors, continuation, NRE, Unobserved and
vertex-limit errors. Full Edit logs retain two intentional exception diagnostics
each; full Play logs retain three each, from the existing negative tests.
There are still four/three Debug warning calls in Edit/Play, one unavailable
licensing access-token notice per Editor, Android's existing Both-input warning,
and a Mono Editor adb target-discovery protocol warning. These are not claimed
as warning-free logs or mobile input/device-discovery success. The Mono Player
log has no exception diagnostic. Review the derived
`stage15-lifecycle-sanitized-logs` and its hash manifest: the adb warning can dump
environment variables, so sensitive environment lines are redacted in review
copies; raw originals remain privately preserved, unchanged and not uploaded.

HEAD/index/branch and all protected hashes are unchanged from the initial
evidence below. Login/Config/fonts/vendor remain untouched, no user Editor was
terminated, and stage 16 has not started. Stage-13 full-frame 222B/Exceeded is
still historical, not reclassified as a performance pass.

## Stage 15 initial boundary evidence (historical final4)

Unity 2022.3.62f2, 2026-11-03 UTC. No HybridCLR package or metadata-generation
toolchain was installed. This stage validates the extension boundary, not a
downloaded-code deployment or mobile device run.

| Gate | Passed / total | Actual XML UTC interval | Duration (s) | XML |
|---|---:|---|---:|---|
| Directed boundary/real DLL/Bootstrap, Edit | 6/6 | 02:50:23-02:50:25 | 1.7546668 | `stage15-directed6-EditMode.xml` |
| Directed loading/constructor/cancel/UI, Play | 5/5 | 02:50:41-02:50:42 | .3042691 | `stage15-directed6-PlayMode.xml` |
| Full installed, Edit | 252/257 | 03:04:46-03:05:19 | 32.5091597 | `stage15-acceptance-installed-final4-EditMode.xml` |
| Full installed, Play | 219/219 | 03:05:33-03:06:03 | 30.2499501 | `stage15-acceptance-installed-final4-PlayMode.xml` |
| Full no-vendor, Edit | 252/257 | 03:04:57-03:05:27 | 29.3477894 | `stage15-acceptance-no-vendor-final4-EditMode.xml` |
| Full no-vendor, Play | 204/204 | 03:05:41-03:06:07 | 25.6821532 | `stage15-acceptance-no-vendor-final4-PlayMode.xml` |

Both full Edit runs retain the same five planned stage-14 Explicit skips.
Their separately accepted stage-14 generator/graphics/bundle/negative-build/
compiled-authoring evidence below is historical; **those five were not rerun
in stage 15** and are not added to this stage's executed count. All eleven new
stage-15 cases run in the full suites without skips. The directed row predates
the final Editor-only report/settings-restoration refinements; all final-source
cases are included in the later full rows. Original stage-9/10 GC assertions
are unchanged. Config tools/workbooks/generated data are unchanged, so Node
was not rerun and dependencies were not installed.

### Requirements → entry → evidence

| Requirement | Entry | Actual check |
|---|---|---|
| Explicit AOT / public-only Hotfix boundary | Hotfix.Sample and CodeLoading asmdefs, compiled Player graph | No Editor/internal/friend access or AOT reverse business dependency; real sample compiles in Editor, Mono Player and IL2CPP |
| Concrete reflected Context contract | `ValidateContextType`, `Register<T>`, `For<T>`, Editor mapping | Abstract/private-ctor/parameterized/non-Context types rejected before publication; both manager Activator paths execute |
| Verified resources → DLL → entry | Existing BootstrapRunner + `ResourceHotfixCodeLoader` / `HotfixGameEntry` | Download verification precedes code asset load; code failure blocks entry; public entry opens real Context |
| Actual DLL bytes, not just named mock | Fresh AssemblyBuilder fixture, imported `.dll.bytes`, `MonoHotfixAssemblyRuntime` | Initially not-loaded identity, binary TextAsset/SHA256, real Assembly.Load(bytes), external public register/open and cleanup |
| Ownership, trust and irreversible failure | Resource lease + injected assembly runtime | Lease zero before code execution; wrong readiness/hash/identity/type never publishes; constructor failure sticky; constructor cancellation retains completed instance without reconstruction |
| Async cancellation / late handles | Existing UIResourceService | Noncooperative provider cancellation never executes code; late handle drains/releases; borrowed owner not shut down |
| Executable generic-risk plan | `HotfixAotWitness`, exported draft | Real int/string property/collection/command/UniTask witness returns 8; exact sample asmdef path/GUID and Runtime/UniTask/mscorlib defining-assembly candidates validated |
| Real linker/native gate | High-stripped Android ARM64 Player build | Actual UnityLinker output retains entry/page public ctors, public entry, private generic Open and Type prewarm paths; IL2CPP C++ generation/native compilation/APK succeed |
| Runtime reflected flow after stripping | High-stripped Windows Mono Player | Process exit 0 and smoke JSON: reflection entry, generic Open, reflected registered prewarm, witness 8, UI shutdown |
| Runnable sample / reviewed draft | Stage-15 Editor menus, AOT host, CI methods | Existing login untouched; real batch export; reviewed output/path safety tested; source-equal validation projects remain byte-identical after tools |

“Abstractions” remains the existing logical public-contract layer in Runtime,
not a fictional extra DLL. The generic risk register distinguishes **linker
retention**, **executed known AOT combinations**, and **future HybridCLR
supplementation of generic method-definition owners**. See
[HybridCLRBoundary.md](HybridCLRBoundary.md) for executable usage and official
Unity/HybridCLR references. No arbitrary future value-type/state-machine or
optimized-release generic coverage is claimed.

### Actual stripping / build / process evidence

`stage15-android-il2cpp-high-final4-build.json` records actual
**Android ARM64 / IL2CPP / High / Development**:
03:04:45.8141645-03:05:44.7079875 UTC, BuildReport **58.3432633s**,
**Succeeded**, no build-report errors, total build size **527,433,172 bytes**.
This is the whole audited minimal-demo project, including its existing built-in
Resources, not a hot-update DLL payload/size benchmark or Production policy pass.
The actual APK is **40,157,216 bytes** (BuildReport's total above is not APK size);
its ZIP entries include `lib/arm64-v8a/libil2cpp.so` (**42,126,560 uncompressed bytes**)
and `assets/bin/Data/Managed/Metadata/global-metadata.dat` (**6,524,200 bytes**).
The evidence records both entries' SHA256, not just an output filename.
`stage15-android-stripped-final4.json` inspects actual
`Library\Bee\artifacts\Android\ManagedStripped` DLLs and records their hashes.
Native generated C++ contains the sample entry/constructors and Type-prewarm
path and was compiled by the already-installed Android toolchain into the APK.
No Android device/emulator execution or code-hot-update runtime success is claimed.

`stage15-mono-high-final4-build.json` separately records
**StandaloneWindows64 / Mono / High / Development**:
03:06:15.929856-03:06:35.7306156 UTC, **19.3713669s**,
**117,075,584 total build bytes**, Succeeded, no report errors.
`stage15-mono-stripped-final4.json` checks that build's real stripped members.
The actual executable exits 0; `stage15-mono-high-final4-smoke.json` and
`-player.log` verify public reflected entry, both Context creation paths,
witness **8** and UI shutdown. This runtime execution is **Mono, not IL2CPP**.

The real Windows IL2CPP attempt is preserved as
`stage15-il2cpp-high3-build.json` / `.log`, **Failed**:
Visual Studio is present without C++ components and Windows SDK is absent.
It is not counted as passed. The existing Android NDK/SDK/JDK enabled the
separate genuine IL2CPP gate; nothing is inferred from static link.xml alone.
iOS build, Android/iOS device execution, actual HybridCLR metadata loading and
hot-update publishing remain outside this stage.

### Isolation, source/Git and diagnostic boundary

Evidence remains outside the repository in the session `files` directory:
`stage15-acceptance-evidence.json` includes XML/log/build/output hashes, real UTC
attributes, native symbols, baseline deltas, complete manifests and Git status.
`stage15-boundary-draft-final.json` is the actual exported draft, not generated
HybridCLR settings/metadata. Failed/interim runs remain preserved separately.

Final installed/Android maps each contain **762 files**; no-vendor **694**,
covering complete Assets/Packages/ProjectSettings. All three projects have
pre-source = pre-copy = post-source = post-copy: **zero missing/extra/mismatch**,
with no post-run resync. Canonical source/copy manifest hashes are:

- installed and Android: `9bf54cffad9c1e81cf6ef973c4d503a872b4d1fda62770dffd80922c5e145cb2`;
- no-vendor: `80ed433b7908344907540a1ce92b52e4dc07c6aea6529e7de03d9d89eab550f5`.

The build probe restores the full native serialized PlayerSettings snapshot,
including absent platform-map defaults, and its original file bytes/line endings
in finally. Earlier runs exposed real map insertion/CRLF drift; those failed hash
checks remain evidence, rather than being hidden by a final resync.
Against the 732-input stage-15 baseline: **30 additions, seven intentional existing
input edits, no removals**. Existing Config/login/assets/fonts/vendor and original
GC tests remain unchanged; relevant old documentation is intentionally extended.
The main Unity Editor was not used for validation or terminated.

HEAD remains `d1b6036fd5b8398fcbf483d707c38294f2112062`,
branch/upstream `YF1.1` / `origin/YF1.1`, index empty. No branch, stage, commit,
push or PR. Protected SHA256 values remain:
`.gitignore` `67BA88166912704B4E15DC3831665DFF60596CEB05EB279E9729CC2C84460694`,
WenKaiFallback `F10804CE54A9D797DA81ADD759B7355BA4E3480D21422FF4F21061ED6004509C`,
local info/exclude `10C0F8FBD5904AE89F7E0C845E05DAE7D565049478887883911C7072E1AC9B5E`.

All six listed XML/log pairs have zero C# warnings/errors, NullReferenceException,
UnobservedTaskException and vertex-limit exceptions. They are **not all-warning/
exception-free logs**: each full Edit retains two intentional diagnostics
(quarantined adapter and invalid monotonic clock), each full Play retains three
(injected material error, its ResourceLoadException chain, quarantined adapter).
Licensing reports one unavailable access token per Editor process while builds/
tests still complete; existing negative font/localization/EventSystem warnings
are not reclassified as new failures. The final Mono Player log has no exception
diagnostic. Stage-13 full-frame GC **222B / Exceeded** remains its historical
Editor/Null result, not a new performance pass or an IL2CPP/device measurement.
The Android build also reports the existing **Active Input Handling = Both**
setting as unsupported on Android. It was not silently changed to make a build
look clean; mobile input behavior remains unvalidated here.

## Complete stage 14 acceptance

Unity 2022.3.62f2, Windows Editor/Mono, 2026-11-02; Node 24.15.0.
Intervals and durations below are actual XML `test-run` attributes, not shell
elapsed time. Every executed test passed; no failure/inconclusive result is
counted as acceptance.

| Gate | Passed / total | UTC interval | Duration (s) | XML |
|---|---:|---|---:|---|
| Directed Editor tools | 26/26 | 11:02:04-11:02:26 | 22.3897976 | `stage14-directed-final3-EditMode.xml` |
| Full installed, Edit | 246/251 | 11:03:33-11:03:59 | 25.9537221 | `stage14-acceptance-installed4-EditMode.xml` |
| Full installed, Play | 214/214 | 11:04:13-11:04:46 | 32.5896641 | `stage14-acceptance-installed4-PlayMode.xml` |
| Full no-vendor, Edit | 246/251 | 11:03:36-11:04:00 | 24.3289227 | `stage14-acceptance-no-vendor4-EditMode.xml` |
| Full no-vendor, Play | 199/199 | 11:04:15-11:04:42 | 27.8335456 | `stage14-acceptance-no-vendor4-PlayMode.xml` |
| Isolated generation/rollback | 1/1 | 11:02:39-11:02:48 | 8.9209593 | `stage14-generator-final3-EditMode.xml` |
| Actual D3D11 preview pixels | 1/1 | 11:05:02-11:05:03 | .5726670 | `stage14-acceptance-graphics4.xml` |
| Official bundle/readback/report | 1/1 | 11:05:16-11:05:21 | 4.9261794 | `stage14-acceptance-bundle4-EditMode.xml` |
| Real negative Player build hook | 1/1 | 11:05:33-11:05:33 | .5288335 | `stage14-acceptance-player-hook4-EditMode.xml` |
| Generated page after compiled regeneration/restart | 1/1 | 11:04:37-11:04:38 | .5387943 | `stage14-acceptance-authoring4-EditMode.xml` |

Each full Edit run has **five planned Explicit skips**, not five untested items:
generator, graphics, official bundle, negative Player build and compiled authoring
are the five isolated rows above. They deliberately need independent Unity
processes, generated assemblies, a real graphics device or Player compilation.
Ordinary directed tests and all Play runs have zero skips. Initial generated-code
compilation and subsequent independent reload also passed 1/1 each:
`stage14-authoring-final-compiled2-EditMode.xml` (10:43:33-10:43:34,
.5875819s) and `stage14-authoring-final-reloaded-EditMode.xml`
(10:43:45-10:43:46, .4552427s). Final regeneration executes the current generator,
preserves its compiled assembly/prefab and then reruns the real page.

Node **33/33**, validation **3 tables**. Fresh existing-client export matches
all **20 owned files/manifests across six directories**, zero differences.
This includes JSON, MessagePack, typed bindings and ownership markers, not merely
row counts. Source workbooks and maintained outputs were not regenerated.

### Requirements to executable evidence

| Requirement | Entry | Regression / actual evidence |
|---|---|---|
| Context/ViewModel/view/config wizard | `UIPageGenerator`, existing Node exporter, generated `Registration.Map` | Actual generated assembly compiles; MonoScript resolves after restart; four serialized controls with duplicate names; `RegisterBatch`, Open/Close and exactly one native release |
| Idempotence and hand-code safety | Owned `.g.cs` hashes, hand partial, workbook proposal | Repeat generation preserves hand edits/row bytes; conflicting generated content/designer rows refused; all eight write-fault boundaries restore input |
| Path/assembly/rollback safety | `SafeFolder`, shared literal-path guard, compilation assembly lookup | Traversal/Editor/enclosing asmdef and actual assembly-name conflict rejected; real Windows junction cannot escape; readonly rollback failure aggregates while other paths/workbook still restore |
| Prefab/scenes and actionable issue location | `UIAssetValidator.InspectHierarchy`, `LocateIssue` | Real deleted serialized asset, MissingScript and required-vs-nullable fields; actual PrefabStage selects second duplicate-named child/property; source bytes unchanged |
| Canvas/input/mask/layout risks | Shared hierarchy validator | Nested sorting Canvas errors; decorative RaycastTarget, duplicate-name, Mask/RectMask and layout/fitter warnings |
| Real resource/package/config gate | `BeginCollect(..., false, false)`, typed tables and actual assets | Real collector duplicate/rule failure, package mismatch/missing UI address, wrong Config mapping, location-lowercase lookup and explicit default package; missing/ambiguous production source cannot pass |
| Text/font/atlas coverage | Existing transient content checks + asset inspector | Deliberate missing key/glyph, overflow, atlas/source/size and actual SpriteAtlas packables/settings; source atlas not packed or rewritten |
| Actual locale/theme/scale preview | Existing `UIDevicePreview`, `UIEditorPresentationPreview` | Real French text/dark color/font scale/Sprite/Material changes; three portrait/landscape safe-area geometries and real D3D11 pixels |
| Preview ownership/error handling | Deep TMP clone/settings scope, inactive visual clone | No business Awake or cloned EventSystem; normal/fault/throwing-dispose paths restore settings and release remaining owned surfaces; no input lock/runtime service added |
| Navigation/resources visualization | Window directed graph and report | Explicit edges/endpoints plus actual dependency and collector mappings; filtering/click location; coverage/skips distinguish unknown business flow |
| Real Player and batch gates | `IPreprocessBuildWithReport`, `UIBuildGate.BuildPlayer` | Actual `BuildPipeline.BuildPlayer` returns failed report from this hook; no exe; unscanned scenes/target policy blocked; real batch valid MinimalDemo exit 0 and missing-profile exit 1 with JSON |
| Demo/production boundary | `UIBuildProfile`, `UIBuildWorkspace.Export` | Production Resources blocked, exact audited MinimalDemo accepted but uncertified; external workspace actually excludes audited files, rejects target dependencies and leaves source unchanged |
| Official local build/report/version/history | `UIYooAssetBuilder` | Actual bundle loads expected TextAsset; official version/report/summary, file SHA256, before-build cancel and duplicate-version failure; prior history preserved |

The prepared authoring project is intentionally **derived**, not claimed as a
source-equal full-suite copy: it adds generated code/prefabs and a twelfth fixture
row. The separate final full-suite copies retain the original eleven rows.
Generation, exporter and compiled binding are explicit stages, not a fictional
single transaction spanning Unity compilation.

### Actual build, graphics and CI outputs

The final official YooAsset 3.0.5 Legacy build is
`StandaloneWindows64/TestPackage/20261102110516780`: **1 asset, 1 bundle,
1,207 bundle bytes**, official integer builder time **1 second**. This is a tiny
local correctness fixture, not a throughput or production-package benchmark.
`stage14-real-bundle-evidence/stage14-last-build.json` records its actual
UTC start/end, official report path/summary and SHA256 for all seven outputs.
The `.version` contents and packaged TextAsset are read back, not inferred from
a successful return string. History is appended outside the project.
Synchronous official builds support cancellation before start only; late
cancellation is explicitly too late, never advertised as a safe abort.

The graphics process uses **Direct3D11 / Intel UHD Graphics 770**, not Null.
`stage14-real-device-preview.png` contains rendered French content and both
unsafe-red/safe-green regions; pixel counts are asserted. An earlier Preview-type
camera produced valid meshes but black output. Those failed runs remain retained;
only the Game-type isolated-camera passing XML is acceptance. Null runs only
certify content/layout checks and report rendering as Unavailable.

`stage14-acceptance-regeneration4.log`,
`stage14-acceptance-batch-valid4.log` and
`stage14-acceptance-batch-invalid4.log` retain actual batch process results.
The latter two have `-report.json` companions: valid **MinimalDemo**, Complete
but not ProductionCertified; missing-profile **Incomplete/Error**, process exit 1.
The negative Player test scopes `LogAssert.ignoreFailingMessages` only around its
expected BuildPipeline failure, verifies the failed report/hook message/no exe,
and restores the previous flag. No global failure suppression is introduced.

### Source/Git boundary and diagnostics

Evidence root remains:
`C:\Users\21093\.copilot\session-state\5943f379-478d-4be4-9574-3b59dc3e9b64\files`.
`stage14-acceptance-evidence.json` records XML/log/output hashes, timing/counts,
diagnostics, batch policy, module capabilities, Git state and protected bytes.
Node logs are `stage14-acceptance-node.log`/`-node-validate.log`;
fresh-output parity is `stage14-generated-parity.json`.
Failed/interim and previous-stage evidence is retained, not counted as passing
final validation. Reports, images, bundles and test artifacts are not in the repo.

Final installed/no-vendor Assets/Packages/ProjectSettings inventories contain
**732 / 664 files**. Pre/post source and copy maps match completely: **zero
missing/extra/mismatch**, without a post-test resync to hide mutations.
Pre maps are `stage14-{installed,no-vendor}-final4-pre-{source,copy}-sha256.json`;
canonical final maps are `stage14-{installed,no-vendor}-final-{source,copy}-sha256.json`.
Their respective source/copy manifest hashes are:

- installed: `4b8f9b8e83d014d269336d5c739a999514f850c25afc370821f19407223256d8`;
- no-vendor: `fb65d4ede1a7c670dfd539d2fcb3b2ec1c18693cb4c622977285b48d2a5fe9f4`.

Against the preserved stage14 start, the 708 original input files have only five
intentional Editor/asmdef changes, **24 additions and no removals**. Runtime
stage12/13 implementation, old GC tests/assertions, Config data, login scene/
prefab/bootstrap, source fonts/materials/atlases and ProjectSettings are unchanged.
The main source Editor was neither used for testing nor terminated.

All twelve listed acceptance XML/log pairs have **zero C# warnings/errors,
NullReferenceException, UnobservedTaskException and vertex-limit exceptions**.
They are not warning-free: licensing logs one unavailable-token diagnostic per
process; full gates retain expected font/localization/EventSystem negative cases.
Each full Edit log has two injected exception messages (quarantined adapter and
invalid monotonic clock). Each full Play has three (material failure, its
ResourceLoadException chain and quarantined adapter). The isolated negative
Player log contains the two expected BuildFailedException messages. These are
asserted producer/negative-path evidence, not unexplained unobserved exceptions.

HEAD remains `d1b6036fd5b8398fcbf483d707c38294f2112062`, branch `YF1.1`,
upstream `origin/YF1.1`, index empty. No stage/commit/push/branch/PR operation.
The original two dirty-file hashes are unchanged:
`.gitignore` = `67ba88166912704b4e15dc3831665dff60596ceb05eb279e9729cc2c84460694`;
`WenKaiFallback.asset` = `f10804ce54a9d797da81add759b7355ba4e3480d21422ff4f21061ed6004509c`.
Vendor sources and `.git/info/exclude` were not edited.

Module capability APIs report Windows/Android/iOS support present, but only the
Windows bundle was built. No mobile Player/device, IL2CPP, Xcode/toolchain,
CDN upload, FPS or mobile budget pass is implied. Original stage9/10 local
allocation gates still pass without altered assertions; stage13's historical
222 B/frame sample remains Exceeded, not an optimization claim from this stage.
Static warnings do not replace runtime profiling. Production certification covers
declared inputs, not inferred arbitrary code-only loads/backend selection; those
must be declared/audited explicitly. The source demo checkout is not presented as
a passed Production project. Stage 15/HybridCLR has not started.
Usage, CI commands and per-check policy: [EditorTooling.md](EditorTooling.md).

## Complete stage 13 acceptance

Unity 2022.3.62f2, Windows Editor/Mono, Null graphics, 2026-11-02.
All intervals/durations below are actual XML `test-run` attributes.
Every gate has zero failed/skipped/inconclusive tests.

| Gate | Passed | UTC interval | Duration (s) | XML |
|---|---:|---|---:|---|
| Directed diagnostic models, Edit | 8/8 | 09:47:07-09:47:07 | .0668525 | `stage13-acceptance-directed5-EditMode.xml` |
| Directed real runtime/panel/profiler, Play | 13/13 | 09:47:20-09:47:23 | 3.4230158 | `stage13-acceptance-directed5-PlayMode.xml` |
| Full installed-vendor, Edit | 220/220 | 09:47:35-09:47:39 | 4.0894363 | `stage13-acceptance-full3-EditMode.xml` |
| Full installed-vendor, Play | 214/214 | 09:47:52-09:48:19 | 27.6207881 | `stage13-acceptance-full3-PlayMode.xml` |
| Full no-vendor, Edit | 220/220 | 09:47:08-09:47:12 | 4.0286856 | `stage13-acceptance-no-vendor3-EditMode.xml` |
| Full no-vendor, Play | 199/199 | 09:47:27-09:47:51 | 24.5701444 | `stage13-acceptance-no-vendor3-PlayMode.xml` |

Evidence root:
`C:\Users\21093\.copilot\session-state\5943f379-478d-4be4-9574-3b59dc3e9b64\files`.
Matching logs and `stage13-acceptance-evidence.json` include XML/log SHA256,
actual timing/counts, log diagnostics, Git state and protected-file hashes.
Extracted full-suite records are `stage13-performance-report.txt`,
`stage13-lifecycle-trace.txt` and `stage13-injected-findings.txt`.
Historical stage12 evidence is retained unchanged; failed/interim stage13 runs
and earlier metadata-normalization passes are not acceptance gates.

The installed validation copy contains **708** Assets/Packages/ProjectSettings
files; the no-vendor copy contains **640**, omitting only the locally installed
commercial integration directory/meta. Final pre/post source/copy inventories
are all identical, with **zero missing/extra/mismatch**. Full per-file manifests:
`stage13-installed-final3-pre-{source,copy}-sha256.json`,
`stage13-no-vendor-final3-pre-{source,copy}-sha256.json` and each project's
post-run `-source-sha256.json`, `-copy-sha256.json`, `-comparison.json`.
Project names are `stage13-validation-project` and `stage13-no-vendor-project`.
Source/copy manifest file SHA256 respectively:
`6db229e694995ef2bbfb394025d289f6f3f588a125970cf496f6825d280c1181` and
`ce5d2f24bbe2c1b058bd6a58d1ba5c246e51b40f0ff33163dee8488f1af97200`.
No source Unity editor or source dynamic font asset was used for these tests.

All six final logs have **zero C# warnings/errors, NullReferenceException,
UnobservedTaskException and uGUI vertex-limit exceptions**. They are not
warning-free: each has one expected adapter-quarantine warning and licensing's
unavailable-access-token diagnostic; Edit gates each contain one injected broken
clock warning. Full gates also retain prior font/material/localization and
duplicate-EventSystem negative diagnostics. No warning suppression was added.
Seven new task-owned meta files were normalized to Unity's generated importer/EOF
format before the final byte-stable runs; no GUID or existing asset was changed.

| Requirement | Runtime/sample entry | Regression evidence |
|---|---|---|
| Pages, states, hierarchy, sorting and both stacks | `UIDiagnostics.Capture`, manager/context IDs, navigator/modal snapshots | Real pending Loading, active page/modal sorting, navigation IDs, pooled reuse and complete terminal lifecycle |
| Loads, leases, pools, locks, focus, animations, subscriptions and errors | Existing resource/pool/input/message/transition sources copied on demand | Actual lease counts, generation/operation IDs, 20 panel Back cycles, restored focus and zero panel locks/subscriptions |
| Correlated Open/Close/Navigate/Load/Transition timings | Existing Context operation IDs plus explicit parent spans | Navigation -> Open -> Load -> NativeLoad/Lease and Transition; unrelated opens not misparented; one terminal per admitted span |
| Success/cancel/fault semantics | Context/transition/resource hooks | Normal destructive Close/Release are Completed despite LifetimeToken cleanup; callback-only cancellation, reverse/interrupt rollback, native faults and exception chains |
| Real leaks and duplicate registration | `LoadOwnedAssetAsync`, exact typed owner/topic/handler snapshots | Actual ended-owner lock/asset lease and bad post-destroy subscriptions detected; legal pool and borrowed global references not misclassified |
| Timeout and noncooperative ownership | `IUIDiagnosticClock`, scope task spans, budget checks | Injected clock exceeds threshold without sleep; canceled scope release remains pending until actual task completion |
| Optional, bounded, fault-isolated observer | Ring/active limits, `Enabled`, explicit `FlushAdapters` | Overflow/copy immutability, disabled hooks do not read clock, bad clock/formatter/adapter cannot break cleanup, incomplete recording never certifies zero residue |
| Real uGUI/native sampling and honest budgets | `UIProfilerSampler`, `UIPerformanceBudget/Report` | Actual enumerated Layout category, dirty-Text Canvas rebuild with nonzero sample calls, real custom marker, unsupported null values and three target profiles |
| Runnable Dev/Editor UI without a second kernel | `UIDiagnosticsSample`, `UIDiagnosticsPanelContext` | Refresh/filter/details, modal/Back/default-focus restoration, actual fault injection/cleanup, bounded text and unchanged root identity |

Detailed tests are `Stage13DiagnosticsEditModeTests` and
`Stage13DiagnosticsPlayModeTests`. The dedicated lifecycle test verifies a cache
hit reaches CachedShowStarted in **<=1 frame**, excludes queue wait/transition
completion and acquires no extra native load.

The **full-suite** profiler scenario is a real sample panel with intentionally
dirty Text geometry, layout and a native custom marker in the isolated Editor.
The report records actual scene `LoadScene`, frame 48841, Editor/Mono and Null
graphics. Last completed samples: Main Thread **3.221 ms**, Canvas.BuildBatch
**.014 ms / 2 calls**, UI Layout/Layout **1.844 ms / 2 calls**, custom probe
**.941 ms / 1 call**, full-frame managed allocation **222 bytes**,
26 active Graphics and 13 active Raycast Targets. These are one observed sample,
not stable-FPS or device results. The **0 B/frame target is Exceeded**, not passed.
Null-renderer DrawCalls/Batches are genuine zero counter readings, not mobile
render certification. CachedOpenToShow is Unavailable in this particular profiler
scenario because it does not exercise a pool hit; the separate lifecycle test does.
The unsupported marker and arbitrary-provider synchronous-load check remain
Unavailable with null values and reasons. Android/mainstream-mobile comparisons
are **NotValidated**, never Pass based on Editor data.

Original stage9/10 tests and assertions are unchanged: typed publish **0 bytes /
10,000**, paired Slider/Toggle **0 bytes / 10,000** (original <=4,096-byte
Editor/Mono control budget), vendor ScrollTo **0 bytes / 100**, continuous content
scroll **0 bytes / 200**, native instances **29 -> 29**. Their original
warmup/local synchronous measurement exclusions remain; they are not whole-frame
or whole-system zero-GC claims.

Config workbooks/tooling, generated settings, existing eleven registrations and
the login startup remain byte-identical to the accepted stage12 inputs. No Node
dependency/schema/export change was made, so stage13 does not replace the
historical Node32/validate3 gate with an invented new run.
No Android/iOS device, IL2CPP, GPU/FPS, CDN, production telemetry SDK, stage14 build
checker or HybridCLR certification is implied. Usage and ownership limits:
[Diagnostics.md](Diagnostics.md).

## Complete stage 12 acceptance

Unity 2022.3.62f2, Windows Editor/Mono, 2026-10-30. Node 24.15.0:
**32/32**, validation **3 tables**. All intervals below are actual XML
`test-run` UTC attributes; every gate has zero failed/skipped/inconclusive tests.

| Gate | Passed | UTC interval | XML |
|---|---:|---|---|
| Directed scope/system models, Edit | 9/9 | 11:36:31-11:36:31 | `stage12-acceptance-directed3-EditMode.xml` |
| Directed lifecycle/real system views, Play | 21/21 | 11:36:43-11:36:45 | `stage12-acceptance-directed3-PlayMode.xml` |
| Full installed-vendor, Edit | 212/212 | 11:37:44-11:37:48 | `stage12-acceptance-full2-EditMode.xml` |
| Full installed-vendor, Play | 201/201 | 11:37:59-11:38:23 | `stage12-acceptance-full2-PlayMode.xml` |
| Full no-vendor, Edit | 212/212 | 11:36:31-11:36:34 | `stage12-acceptance-no-vendor2-EditMode.xml` |
| Full no-vendor, Play | 186/186 | 11:36:49-11:37:09 | `stage12-acceptance-no-vendor2-PlayMode.xml` |

Evidence root:
`C:\Users\21093\.copilot\session-state\5943f379-478d-4be4-9574-3b59dc3e9b64\files`.
Matching logs, `stage12-acceptance-node.log`, `stage12-generated-parity.json` and
`stage12-acceptance-evidence.json` are retained there, not in the repository.
The installed validation copy contains **691** Assets/Packages/ProjectSettings
files; the no-vendor copy contains **623**, omitting only the locally installed
commercial integration directory/meta. After final runs, both per-file SHA256
source/copy manifests have **zero missing/extra/mismatch**. Each has
`{project-name}-source-sha256.json`, `-copy-sha256.json`, `-comparison.json`.
Project names are `stage12-validation-project` and `stage12-no-vendor-project`.
The original source Editor and protected dynamic font asset were not used for tests.

All six final logs have **zero C# warnings/errors, NullReferenceException and
UnobservedTaskException**. They are not warning-free: full gates retain the
intentional missing-U+0378, injected material failure, localization
fallback/missing-key/invalid-format and duplicate-EventSystem negative diagnostics.
Each run also logs Unity licensing's unavailable access-token diagnostic; this
did not prevent execution or XML completion. Failed/interim runs remain evidence
but are not counted as acceptance.

Fresh transactional Config export in a scratch project exactly matches all three
maintained JSONs, three MessagePacks, generated bindings and three ownership
manifests. The original six UISettings records compare unchanged against HEAD;
five explicit `system` registrations bring the total to eleven. No generated
schema/type or dependency change is required.

| Requirement | Runtime/sample entry | Regression evidence |
|---|---|---|
| Scene/Global/Module ownership | `IUIService.Scopes`, `UIScope.OpenAsync/PushAsync/RunAsync/ReleaseAsync` | Real active-scene change, additive/same-name handles, rapid A-B-C/late native load, global/module retention, hidden navigation restoration, same-type handoff, 100 teardown cycles |
| Cancel and drain old work/resources | `UIManager.Scopes`, scoped navigator FIFO | Guard/in-flight Close cancellation, noncooperative drain, prewarm/active leases, aggregate cleanup failures; legacy idle-only pool contract retained |
| Multi-owner Loading | `UILoadingService.Begin`, `SampleLoadingContext` | Independent/out-of-order handles, weighted/indeterminate progress, caller/scope/service cancellation, admission rollback, pending-open cancellation and actual modal visibility |
| Bounded Toast | `UIToastService.Enqueue/Tick`, `SampleToastContext` | 10,000 merges keep one item, deadlines unchanged, capacity/priority/FIFO/eviction, rate/gap clock, scope isolation and every terminal ticket outcome |
| Stable confirmation | `UIConfirmationService<T>`, `UIConfirmationContext` | Confirm/decline/double-click/external close, caller/scope/queue/service cancellation, close rollback, shared concurrent shutdown, real buttons/focus |
| System policy extensions | `UISystemUIService<T>`, `SampleSystemContext` | Global disconnected Retry, maintenance acknowledgement and force-update boundary; scene change preserves notice |
| Unified input | `IUIService.InputArbiter`, `UIInputRouter` | Existing nested whitelist locks/modal stack, fullscreen/selection/pointer state, modal Back decline versus Loading/force-update block, no covered-page pop |
| Runnable integration | `SystemUISample`, explicit `system` Config mapping | Real code-built views, progress/cancel buttons, bounded displayed Toast, safe content and existing focus/transition runtime; login startup unchanged |

Stage 9/10 allocation tests and assertions are unchanged and included in the full
gates: typed publish **0 bytes / 10,000**, paired Slider/Toggle updates **0 bytes /
10,000** (original Editor/Mono control budget <= 4,096 bytes), vendor ScrollTo
**0 bytes / 100** and continuous content scroll **0 bytes / 200**, with native
instances **29 -> 29** and the original warmup/exclusion scopes. Stage 12 makes no whole-system
zero-allocation claim. No CDN, Android/iOS device, IL2CPP, FPS, real networking,
stage-13 diagnostics or stage-14 build-tool certification is implied.

## Complete stage 11 acceptance

Unity 2022.3.62f2, Windows Editor/Mono, Direct3D11 (Intel UHD Graphics 770),
2026-10-10. Node 24.15.0: **31/31**, validation **3 tables**.
All times below are fresh XML `test-run` UTC attributes, not command timestamps.

| Gate | Passed | UTC interval | XML |
|---|---:|---|---|
| Directed text/presentation/Inspector, Edit | 20/20 | 09:06:04-09:06:04 | `stage11-acceptance-directed2-editmode.xml` |
| Directed controls/render/lifecycle, Play | 17/17 | 09:06:15-09:06:18 | `stage11-acceptance-directed2-playmode.xml` |
| Full installed-vendor, Edit | 195/195 | 09:07:06-09:07:10 | `stage11-acceptance-final-editmode.xml` |
| Full installed-vendor, Play | 170/170 | 09:07:23-09:07:45 | `stage11-acceptance-final-playmode.xml` |
| Full no-vendor, Edit | 195/195 | 09:08:00-09:08:04 | `stage11-acceptance-no-vendor-final-editmode.xml` |
| Full no-vendor, Play | 155/155 | 09:08:15-09:08:34 | `stage11-acceptance-no-vendor-final-playmode.xml` |

Evidence root:
`C:\Users\21093\.copilot\session-state\7310900a-0fd1-4a43-bd14-c674e24903bf\files`.
Matching `.log` files, `stage11-acceptance-node-final.log` and
`stage11-acceptance-evidence.json` are retained there.
Copies are `stage11-validation-project` (**630** Assets/Packages/ProjectSettings
files) and `stage11-no-vendor-project` (**562**, omitting only local commercial
integration and its meta). Each has `{project-name}-source-sha256.json`,
`-copy-sha256.json`, `-comparison.json`, verified after the final runs with
**zero missing/mismatch/extra**. Earlier Config/text copies and all failed/interim
logs remain evidence, not acceptance results.

Final logs have **zero C# warning/error, Unobserved and NullReference diagnostics**.
They intentionally contain missing-U+0378, injected material-load failure,
localization fallback/missing-key/format and legacy duplicate-EventSystem negative
test diagnostics. No warning global suppression or removed regression assertions
were used.

Real WenKai evidence: `stage11-validation-project\stage11-wenkai-raster.png`.
The actual TMP font renders Chinese/Latin/French, 38 visible glyphs, 152 vertices,
11,383 nonblack ink pixels; primary has 160 prewarmed characters and one 2048-square
Alpha8 atlas. A transient clone of the configured dynamic fallback adds 300 CJK
characters in one 2048-square atlas (4,194,304 texture bytes maximum). The clone
prevents test growth from modifying the source font. This is not full Unicode,
device rendering/60 FPS, mobile/IL2CPP or whole-UI zero-GC evidence.

Protected gates retained their original measurement scope:
typed publish **0 bytes / 10,000**; real Slider/Toggle paired updates **0 bytes /
10,000**; vendor ScrollTo **0 bytes / 100**, native instances 29 -> 29;
continuous vendor content scroll **0 bytes / 200**, 29 created.
Player-loop/test-runner/deferred uGUI layout and string formatting are excluded
where originally documented. The new presentation layer makes no zero-GC claim.

Stage11 regressions cover six device geometries and 18 ratio/font-scale combinations,
scrollable focus, scaler/safe-area restoration, parent geometry changes, typed
tokens, direct/caller cancellation, partial/late assets, material rendering,
close rollback/failed show/Shutdown, 1,000 disable cycles and read-only Editor
font isolation. Read-only review fixes: TMP materials must use fontSharedMaterial;
safe content must refresh after nested parent changes; transient previews must
clone shared font/atlas dependencies. Directed investigation also fixed concurrent
awaits of refresh results, completed TMP punctuation resources and prewarmed common
currency/control glyphs so final full-copy hashes stay unchanged.

## Stage 11 text-only localization verification

Historical checkpoint before the complete-stage font/presentation work above.

Unity 2022.3.62f2, 2026-10-10. Node 24.15.0: **29/29** plus validation of both
workbooks. All intervals are actual XML `test-run` UTC attributes.

| Gate | Passed | UTC interval | XML |
|---|---:|---|---|
| Directed localization / Inspector, Edit | 14/14 | 04:46:44-04:46:44 | `localization-acceptance-directed-editmode.xml` |
| Directed real controls / lifecycle, Play | 6/6 | 04:46:59-04:47:02 | `localization-acceptance-directed-playmode.xml` |
| Full, installed SuperScrollView, Edit | 189/189 | 04:47:29-04:47:32 | `localization-acceptance-final-editmode.xml` |
| Full, installed SuperScrollView, Play | 159/159 | 04:47:46-04:48:08 | `localization-acceptance-final-playmode.xml` |
| Full, no vendor, Edit | 189/189 | 04:50:12-04:50:16 | `localization-acceptance-no-vendor-final-editmode.xml` |
| Full, no vendor, Play | 144/144 | 04:50:30-04:50:49 | `localization-acceptance-no-vendor-final-playmode.xml` |

Evidence root:
`C:\Users\21093\.copilot\session-state\7310900a-0fd1-4a43-bd14-c674e24903bf\files`.
Matching `.log` files, `localization-acceptance-node.log` and
`localization-acceptance-evidence.json` are retained there.
Validation roots directly under it are `localization-validation-project` (557
Assets/Packages/ProjectSettings files including installed vendor) and
`localization-no-vendor-project` (489 files). Each has `{project-name}-source-sha256.json`,
`-copy-sha256.json` and `-comparison.json`: zero missing/mismatch/extra at verification.
The earlier Config validation copies remain untouched. The unchanged UISettings JSON
and bytes additionally match their earlier Config-acceptance SHA256 manifest.
`localization-source-reference-sha256.json` records hashes of seven read-only source
references at delivery, not a retroactive before/after source baseline.

All six gates have zero failed/skipped/inconclusive results; logs have zero C#
warning/error, Unobserved and NullReference diagnostics. They are **not warning-free**:
active TMP tests temporarily inject/restore isolated TMP settings because this repo
has no Essential font assets, and report missing-font warnings. They exercise actual
TextMeshProUGUI lifecycle and string updates, not glyph rendering. Tests also
deliberately log fallback/missing-key/invalid-format diagnostics and the pre-existing
duplicate EventSystem regression. No interactive Unity editor was terminated.

Regressions cover Unicode JSON/MessagePack equivalence, strict schema/key/locale/format
failure before export/publication, fallback versus exact API results, before-ready
binding, Config reload/shutdown/reinit, cancellation and late noncooperative results,
read-only entries, picker multi-edit/Undo, real Text/TMP enable/disable/destroy/rebind,
1,000 real display/pool cycles with subscriber count returning to zero, navigation
coverage/resume, canceled-close rollback, failed show cleanup, and actual migrated
startup/language-button behavior. Existing Config, MVVM and commercial-list suites
remain included.

Focused read-only review found and regressed: nested Config notification incorrectly
clearing the outer language mutation guard; repeated already-open sample shows
accumulating language listeners/retaining a previous text binding; and export accepting
locale names not recognized by Unity. The fixes restore the outer guard, replace the
sample binding group, and enforce the same bounded locale allowlist in export/runtime.
No localized font/sprite, safe-area/theme, IL2CPP/mobile/CDN, glyph-rendering, FPS or
localization zero-allocation claim is made.

## Independent Config integration acceptance

Unity 2022.3.62f2, 2026-10-09. Exact intervals below are from the XML `test-run`
attributes (UTC), not the Unity process window. Node 24.15.0 exporter tests: **17/17**;
`npm.cmd run validate` also succeeds on the one six-row UI workbook.

| Gate | Passed | UTC interval | XML |
|---|---:|---|---|
| Directed Config + verified Bootstrap composition, Edit | 11/11 | 07:30:48-07:30:48 | `config-acceptance-directed-editmode.xml` |
| Directed Config UI/resources, Play | 5/5 | 07:36:49-07:36:52 | `config-acceptance-directed2-playmode.xml` |
| Full, installed SuperScrollView, Edit | 175/175 | 07:37:05-07:37:08 | `config-acceptance-final2-editmode.xml` |
| Full, installed SuperScrollView, Play | 153/153 | 07:37:23-07:37:42 | `config-acceptance-final2-playmode.xml` |
| Full, no vendor, Edit | 175/175 | 07:37:56-07:37:59 | `config-acceptance-no-vendor-final2-editmode.xml` |
| Full, no vendor, Play | 138/138 | 07:38:14-07:38:30 | `config-acceptance-no-vendor-final2-playmode.xml` |

All six gates have zero failed/skipped/inconclusive tests. Their matching logs have
zero C# warning/error, Unobserved or NullReference diagnostics. Full Play logs still
include the deliberately expected duplicate-EventSystem error in its existing regression.
No user's interactive Unity process was terminated.

Evidence root:
`C:\Users\21093\.copilot\session-state\7310900a-0fd1-4a43-bd14-c674e24903bf\files`

Validation projects are directly under that root:
`config-validation-project` (531 Assets/Packages/ProjectSettings files, including local
licensed integration) and `config-no-vendor-project` (463 files).
For each project, `{project-name}-source-sha256.json`, `-copy-sha256.json` and
`-comparison.json` record the complete inputs and zero missing/mismatched/extra files.
`config-source-readonly-sha256.txt` and `config-source-postintegration-comparison.json`
verify all 13 inspected MatchingGo source files stayed unchanged.
`config-acceptance-node-final.log` contains the Node results. These retained copies and
manifests are session evidence, never framework release assets.

Coverage includes protocol/long/compound keys and actual Node MessagePack output in Unity,
all six directory-swap failure points, retained metas/unmanaged files, malformed Excel,
UI schema/codegen validation, required/optional atomic snapshots, retry/reload, shared
caller cancellation, late noncooperative results and shutdown/reinit, TextAsset/native
lease cleanup, package-aware prewarm/open, batch zero-partial registration and active
re-registration rejection, all migrated profiles, actual page navigation, and verified
Bootstrap -> Config -> UI registration -> business success/failure ordering.

Focused read-only review regressions reject enclosing/generated member collisions,
reject nested JSON5 nonfinite values before JSON/MessagePack divergence, and exclude
optional tables whose asset disposal failed. Actual migrated examples also exposed the
old Arial.ttf runtime exception and singleton button dispatch; these are fixed and covered.
Config does not claim zero-GC loading or device FPS. Existing stage9/10 allocation
measurements remain separate full-suite regressions.

## Test assemblies

- `YUIFramework.Tests.EditMode`: deterministic framework data structures and contracts,
  including stage 5 resource keys/package registry and stage 6 profile, state graph, runner,
  retry/fallback/lifecycle, progress/telemetry, concurrency, and YooAsset URL adapter tests
  (`ResourceOwnershipEditModeTests`, `BootstrapCoreEditModeTests`,
  `BootstrapRunnerEditModeTests`, `YooAssetBootstrapAdapterEditModeTests`).
- `YUIFramework.Tests.PlayMode`: GameObject, lifecycle, pooling, UIRoot, and navigation
  behavior, plus the stage 5 resource ownership suites
  (`ResourceOwnershipPlayModeTests`, `UIManagerResourceOwnershipPlayModeTests`).
  Stage 7 adds deterministic pool-policy EditMode tests and PlayMode coverage for distinct
  instance prewarm, cancellation/failure rollback, scene/module scopes, display cleanup,
  low-memory lease safety, and 1,000-cycle reuse stability
  (`PoolingGovernanceEditModeTests`, `PoolingGovernancePlayModeTests`).
  Stage 8 adds real visual assertions for interruption/reverse/skip, unscaled time,
  custom-transition failure recovery, navigation coverage/suspension, stale-continuation
  fencing after forget/disposal, curve snapshots, pool-rebind baselines, and 1,000-cycle
  baseline stability (`Phase8TransitionsPlayModeTests`).
  Stage 9 adds typed-message ordering/mutation/error/scope/GC tests, command and validation
  contracts, real uGUI/TMP binding tests, and 1,000-cycle display/lifetime/VM ownership
  coverage (`Stage9MessagingMvvmEditModeTests`,
  `Stage9BindingsLifecyclePlayModeTests`).

The PlayMode assembly references `YUIFramework.Runtime` and UniTask. EditMode additionally
references `YUIFramework.Bootstrap` and `YUIFramework.Bootstrap.YooAsset`. Stage 5 drives a
fake `IUIResourceProvider`; stage 6 drives a fake `IBootstrapBackend`. No bootstrap test uses
a real network endpoint.

Review regressions cover unsafe package/version path segments, cumulative multi-package disk
reservation, cancellation during non-cooperative code/game entry, Reset-versus-Shutdown
serialization, frozen provider identity, shared resource shutdown, and propagated
provider-shutdown failures.
The final review regressions also cover timed-out-operation fencing, reset failure followed by
backend shutdown, failed native release retry during shutdown, invalid pooled entries of a
different context type, and traversal rejection in the hardened relative-path builder shared
by production and compatibility URL resolution.
Closeout coverage rejects stale progress from a timed-out download and verifies that cleanup
continues after reset failure. Compatibility launch ownership and faulted resource-shutdown
retry are covered by compilation and resource lifecycle regressions.

## Run from Unity

Open **Window > General > Test Runner**, then run EditMode and PlayMode suites.

## Run from command line

```powershell
# Some installs expose the editor as "2022.3.62f2-x86_64"; use whichever path exists.
$unity = "C:\Program Files\Unity\Hub\Editor\2022.3.62f2-x86_64\Editor\Unity.exe"

& $unity -batchmode -nographics `
  -projectPath "D:\mywork\YUIFramework" `
  -runTests -testPlatform EditMode `
  -testResults "D:\mywork\YUIFramework\TestResults-EditMode.xml" `
  -logFile "D:\mywork\YUIFramework\TestResults-EditMode.log"

& $unity -batchmode -nographics `
  -projectPath "D:\mywork\YUIFramework" `
  -runTests -testPlatform PlayMode `
  -testResults "D:\mywork\YUIFramework\TestResults-PlayMode.xml" `
  -logFile "D:\mywork\YUIFramework\TestResults-PlayMode.log"
```

Generated result and log files are local validation artifacts and must not be committed.

## Stage 6 acceptance

Unity `2022.3.62f2` final gate on 2026-09-04:

| Suite | Result | UTC interval | XML |
|---|---:|---|---|
| EditMode | 133 passed, 0 failed, 0 skipped | 10:58:43-10:58:46 | `phase6-final-editmode.xml` |
| PlayMode | 98 passed, 0 failed, 0 skipped | 10:59:21-10:59:32 | `phase6-final-playmode.xml` |

Both final logs contain zero `warning CS`, `error CS`, `Unobserved`, or `NullReference`
matches. XML and logs are retained in the session artifact directory, not the repository.

## Stage 7 acceptance

Unity `2022.3.62f2` final gate on 2026-09-08:

| Suite | Result | UTC interval | Persistent XML |
|---|---:|---|---|
| EditMode | 144 passed, 0 failed, 0 skipped/inconclusive | 11:18:01-11:18:04 | `C:\Users\21093\.copilot\session-state\7310900a-0fd1-4a43-bd14-c674e24903bf\files\phase7-final-editmode.xml` |
| PlayMode | 107 passed, 0 failed, 0 skipped/inconclusive | 11:18:15-11:18:27 | `C:\Users\21093\.copilot\session-state\7310900a-0fd1-4a43-bd14-c674e24903bf\files\phase7-final-playmode.xml` |

Both final logs contain zero `error CS`, compilation-failure, `Unobserved`, or
`NullReferenceException` matches. Stage 7 directed coverage is 6 EditMode and 9 PlayMode
tests; the PlayMode suite includes a 1,000-cycle instance/native-count stability regression.

## Stage 8 acceptance

Unity `2022.3.62f2` final post-review gate on 2026-09-15:

| Suite | Result | UTC interval | Persistent XML |
|---|---:|---|---|
| EditMode | 146 passed, 0 failed, 0 skipped/inconclusive | 07:05:35-07:05:38 | `C:\Users\21093\.copilot\session-state\7310900a-0fd1-4a43-bd14-c674e24903bf\files\phase8-final-editmode.xml` |
| PlayMode | 118 passed, 0 failed, 0 skipped/inconclusive | 07:06:08-07:06:21 | `C:\Users\21093\.copilot\session-state\7310900a-0fd1-4a43-bd14-c674e24903bf\files\phase8-final-playmode.xml` |

Both final XML files exist and both logs contain zero `error CS`, compilation-failure,
`Unobserved`, or `NullReferenceException` matches. Stage 8 directed PlayMode coverage is
11 tests, including a 1,000-cycle non-default visual-baseline regression.

Supplemental stale-continuation acceptance on 2026-10-08:

| Suite | Result | UTC interval | Persistent XML |
|---|---:|---|---|
| EditMode | 146 passed, 0 failed, 0 skipped/inconclusive | 05:05:00-05:05:03 | `C:\Users\21093\.copilot\session-state\7310900a-0fd1-4a43-bd14-c674e24903bf\files\phase8-acceptance-final-editmode.xml` |
| PlayMode | 125 passed, 0 failed, 0 skipped/inconclusive | 05:05:15-05:05:28 | `C:\Users\21093\.copilot\session-state\7310900a-0fd1-4a43-bd14-c674e24903bf\files\phase8-acceptance-final-playmode.xml` |

The supplemental directed PlayMode gate is 18/18. It deterministically delays a built-in
continuation across `Forget`/`Dispose`, verifies registry-identity fencing and disposal
callback isolation, samples reverse continuity and `AnimationCurve` progress mid-operation,
and validates explicit baseline refresh plus pooled rebind without drift. Both full logs
contain zero `error CS`, compilation-failure, `Unobserved`, or `NullReferenceException`
matches.

## Stage 9 acceptance

Unity `2022.3.62f2` final post-review gate on 2026-10-08:

| Suite | Result | UTC interval | Persistent XML |
|---|---:|---|---|
| Directed EditMode | 14 passed, 0 failed, 0 skipped/inconclusive | 08:49:18 | `C:\Users\21093\.copilot\session-state\7310900a-0fd1-4a43-bd14-c674e24903bf\files\phase9-acceptance-final-directed-editmode.xml` |
| Directed PlayMode | 8 passed, 0 failed, 0 skipped/inconclusive | 08:49:55-08:49:56 | `C:\Users\21093\.copilot\session-state\7310900a-0fd1-4a43-bd14-c674e24903bf\files\phase9-acceptance-final-directed-playmode.xml` |
| Full EditMode | 160 passed, 0 failed, 0 skipped/inconclusive | 08:50:51-08:50:54 | `C:\Users\21093\.copilot\session-state\7310900a-0fd1-4a43-bd14-c674e24903bf\files\phase9-acceptance-final-editmode.xml` |
| Full PlayMode | 133 passed, 0 failed, 0 skipped/inconclusive | 08:51:17-08:51:31 | `C:\Users\21093\.copilot\session-state\7310900a-0fd1-4a43-bd14-c674e24903bf\files\phase9-acceptance-final-playmode.xml` |

An interactive editor owned the checkout during the final gate, so the runs used the
session-local `phase9-validation-project`. Before the final suites, SHA-256 manifests for
all 386 files under `Assets`, `Packages`, and `ProjectSettings` matched the checkout exactly
(zero missing, mismatched, or extra source files). No repository file was written by the
validation editor.

The full logs contain zero `warning CS`, `error CS`, compilation-failure, `Unobserved`, or
`NullReferenceException` matches. `GC.GetAllocatedBytesForCurrentThread` reports **0 bytes
over 10,000 warmed typed publishes** and **0 bytes over 10,000 paired
`ObservableProperty<float/bool>` to real inactive `Slider`/`Toggle` updates** in Editor/Mono.
The binding regression enforces a 4,096-byte total budget to tolerate bounded Unity/Editor
bookkeeping while rejecting per-update allocation. These measurements exclude subscription
mutation, formatting strings, other Unity controls, device profiling, and frame-rate claims.

## Stage 10 acceptance

Unity `2022.3.62f2`, Editor/Mono, post-review gates on 2026-10-09. Each row has zero
failed/skipped/inconclusive tests. Times are the XML `test-run` UTC interval, not process
startup/shutdown times. All files are retained under:

`C:\Users\21093\.copilot\session-state\7310900a-0fd1-4a43-bd14-c674e24903bf\files`

| Suite | Passed | UTC interval | XML file |
|---|---:|---|---|
| Directed core EditMode | 4/4 | 03:05:28 | `phase10-final-directed-rerun-editmode.xml` |
| Directed real-plugin PlayMode | 15/15 | 03:05:44-03:05:48 | `phase10-final-directed-rerun-playmode.xml` |
| Full installed EditMode | 164/164 | 03:09:33-03:09:36 | `phase10-final-editmode.xml` |
| Full installed PlayMode | 148/148 | 03:09:51-03:10:08 | `phase10-final-playmode.xml` |
| No-vendor full EditMode | 164/164 | 03:07:49-03:07:52 | `phase10-no-vendor-final-editmode.xml` |
| No-vendor full PlayMode | 133/133 | 03:10:23-03:10:37 | `phase10-no-vendor-final-playmode.xml` |

Each matching `.log` has zero C# warning/error, compilation-failure, `Unobserved`, or
`NullReference` diagnostics. The full PlayMode runs intentionally log the existing duplicate
EventSystem warning in `DuplicateRuntimeAndEventSystem_AreRejectedDeterministically`.
A fresh compilation had exposed an obsolete-facade warning in the legacy HotUpdate
cancellation regression; a narrowly scoped CS0618 suppression now documents that deliberate
compatibility call, without disabling warnings globally.

The installed project is `files\phase10-validation-project`; the no-vendor project is
`files\phase10-no-vendor-project`. Both contain actual `Assets`, `Packages`, and
`ProjectSettings` roots. The user-owned interactive editor was not terminated. Full source
and validation-copy SHA256 manifests were compared after execution:

| Evidence pair (in the same artifacts directory) | Files | Result |
|---|---:|---|
| `phase10-validation-source-sha256.txt` / `phase10-validation-copy-sha256.txt` | 462 | 0 missing/mismatched/extra, including local vendor |
| `phase10-no-vendor-source-sha256.txt` / `phase10-no-vendor-copy-sha256.txt` | 394 | 0 missing/mismatched/extra, excludes optional installation |
| `phase10-vendor-source-sha256.txt` / `phase10-vendor-installed-sha256.txt` | 35 | 14 C# runtime scripts plus version/metas; supplier files unchanged |
| `phase10-adapter-source-sha256.txt` / `phase10-adapter-installed-sha256.txt` | 28 | Original adapter/example/tests/assembly templates match compiled copies |

`phase10-validation-comparison.txt` records paths, counts, git HEAD, and the preserved
`.gitignore` hash. Five newly generated, local-only folder metas were copied from validation
to the previously meta-less installation to preserve their GUIDs; no code was changed to
produce parity. Keep the projects and manifests until acceptance is independently verified.

`Stage10ListContractsTests` pins duplicate rejection, stable selection, mutation-error
aggregation/reentrancy, binding generation, and actionable missing-plugin failure.
`SuperScrollViewPlayModeTests` drives the actual installed native backend. It covers:

- 10,000 rows, scroll targets every 137 indices, bounded native items and correct data:
  vertical 29 created / 17 bound, horizontal 15 / 9, Grid 52 / 52 at the final sample.
- Replace-only local refresh, insert/remove/move/reset/clear, stable selection and anchor
  offset, Grid positions, dynamic horizontal/vertical sizes and end-of-list scrolling.
- Delayed/noncooperative sprite results, A-to-B reuse, offscreen/hide/external destruction,
  caller cancellation while loading the prefab, real Image/Sprite and service lease counts,
  zero outstanding leases and zero duplicate native releases at teardown.
- 1,000 direct displays (20 created, zero bound after hide), plus 1,000 actual UIManager
  pooled open/close cycles, navigation suspension/resume, canceled close, and shutdown.
- Error recovery and per-item aggregate cleanup; real EventSystem touch rebasing,
  parent/child direction arbitration, native List/Grid drag-end cleanup and input denial.

The focused read-only review found and fixed four issues: copied native rebase events
replaced touch identity; routing/disable omitted native drag co-handlers; temporary-pool
legacy rebind skipped unbind; one throwing cancellation callback interrupted suspension
cleanup. Their regressions pass without modifying the vendor kernel.

Allocation measurement uses `GC.GetAllocatedBytesForCurrentThread` after 100 warmup scrolls.
The real native-plus-adapter `ScrollTo` path allocated **0 bytes / 100 calls**, retaining
29 native items before/after; continuous content movement plus `UpdateListView` allocated
**0 bytes / 200 steps**, still 29 created. Each interval enforces a **4,096-byte total
budget**, excludes assertions/object construction, and has no sprite binder (zero sprite
leases). These are measured synchronous hot-path batches, not whole rendered frames:
player-loop/test-runner and deferred uGUI rebuild allocations, label formatting, async
loading/CTS, device profiling and 60 FPS claims are explicitly excluded.

## Characterization-test rule

Phase 0 tests freeze observable Y1 behavior. A later phase may intentionally change that behavior only when it:

1. adds the replacement Y2 test,
2. updates the migration matrix,
3. documents the behavior change, and
4. retains the temporary compatibility behavior when required.

## Isolation requirements

- Tests must release active contexts and clear pools.
- Tests must clear navigation and message state.
- PlayMode tests must destroy generated prefabs, UIRoot, and EventSystem objects.
- A test must not depend on execution order.
- Production tests must not use real CDN endpoints.
- Every full-suite run must write a fresh persistent XML result and scan its log for
  `warning CS`, `error CS`, `Unobserved`, and `NullReference`.
