# Y2.0 release candidate gates

Candidate: **2.0.0-rc.1**, Unity **2022.3.62f2**. This checklist deliberately
distinguishes implemented tooling, executed evidence and external blockers.
**It is not a fully certified commercial/mobile release.**

| Requirement | Implemented entry | Required acceptance evidence |
|---|---|---|
| One source, UPM Runtime/Editor/Tests/Samples | `UIPackageExporter`, Distribution manifest, asmdefs | Two byte-identical exports, post-import verify, no local commercial/user-dirty/validation files |
| New nonembedded project | PackageQuickstart + consumer manifest | No-Import compilation and package Edit/Play XML with testables |
| Real sample import | package samples declaration + Unity Sample.Import | Actual Assets import, compiled business asmdefs, source/linker roots inspected |
| Complete flow | `ReleaseCandidateSample` / `ReleaseCandidateFlow` | Real Bootstrap/YooAsset marker, navigation, modal, VM/list, locale, scene drain/global survival, shutdown/template assertions |
| Y1 upgrade | Migration + retained bridges scheduled 2.1.0 | Original Hello sample through instance/Config/presentation path and owned template cleanup, not only new sample success |
| Reflected construction/stripping | prewarm Type path + generic Open, imported Hotfix roots | Actual High IL2CPP native build, retained-member inspection; separate execution evidence |
| YooAsset reflective file systems | core `UIYooAssetLinker`, no sample dependency | Actual linker callback/output and stripped Builtin/Sandbox default ctors; native Offline resource initialization/marker run |
| Full framework regression | existing installed/no-vendor suites | Actual Edit/Play XML UTC/counts, unchanged stage-9/10 allocation assertions, isolated font state and full source/copy maps |
| Build-before checks | `UIBuildGate`, `MobilePreflight`, official bundle builder | Located deliberate failures and target-specific reports, audited MinimalDemo distinguished from Production |
| Output protection | shared RC/Player external-path preflight + create-new reports | Relative/internal/ancestor/existing/linked/conflicting inputs rejected before build effects; marker/report bytes preserved; real valid-output build |
| Android | `BuildDevelopmentSmoke`, dedicated application ID/report | Development ARM64 IL2CPP High build **and independently observed device core-UI run** |
| iOS | same UI validation + iOS host/module checks | Native signed Development build and authorized iOS device smoke on macOS/Xcode; Windows cannot certify this |
| API/docs/resource/lifetime/performance/support | PublicAPI, domain contracts, Troubleshooting | APIs/sample behavior match; no Editor FPS/full-frame/unknown generic claims |
| Commercial redistribution | Distribution/NOTICE, third-party notices | Framework owner chooses/grants the intended license; local vendor rights remain separate |

## External blockers, not waived gates

Local implementation, fresh nonembedded package/import tests, all five separate
Editor producer/renderer checks, full installed/no-vendor regressions, real
Android High native build and independent desktop Offline/Hotfix runs have
actual evidence in Testing.md. Android's latest authorized installation succeeded,
but the activity paused before Unity and produced no valid current-run report or
screenshot. **Android device core-UI acceptance is not passed**; no further phone
action is attempted during unattended handoff.

On this Windows host, native iOS signing/Xcode/device execution requires an
external authorized macOS/iOS environment. No cloud host, paid service or SDK
installation is created to hide that absence. Until actual iOS native/device
evidence is supplied, the original mobile gate and full RC acceptance remain
**Blocked**, even if Android passes.

Author-code redistribution rights are not specified in the repository. The
private manifest's UNLICENSED value does not grant them. No public publication
or claim of a licensed commercial package is made.

Android device UI smoke is a functionality check, not a 60-FPS, full-frame 0-B,
touch/accessibility, long soak, rotation, CDN or code-hot-update certification.
Unavailable counters and unmeasured mobile budgets remain explicit. HybridCLR
installation, AOT supplemental metadata generation and DLL publishing remain out
of scope. Preserve the original stage-13 GC Exceeded observation.

## Evidence discipline

Store XML/logs/Players/APKs/maps/reports outside the repository. Record the exact
source HEAD plus dirty input hashes, package inventory, target/backend/stripping,
true UTC run windows, test result counts, producer diagnostics and actual device
versus automated-event scope. Do not synchronize after tests to conceal drift.
Keep failed iteration evidence separate from the final acceptance index and use
redacted logs for review. The current executed results are appended to
[Testing.md](Testing.md); historical results there remain historical.
