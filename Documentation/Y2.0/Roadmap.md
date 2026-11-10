# YUIFramework Y2.0 Roadmap

Y2.0 uses one development line and advances through strict stage gates. Only one stage is implemented at a time. Code, tests, documentation, and acceptance must be complete before the next stage begins.

**Progress: stages 0-15 complete. Stage 16 local implementation and executable
host gates are delivered for review; original acceptance remains blocked by
Android device UI smoke and native iOS/device evidence. No mobile gate is waived.**

Independent post-stage-10 work: **Config integration complete**. This connects the
Excel toolchain, atomic typed snapshots and existing UI registrations; it does not
renumber the roadmap. Acceptance: Node 17/17, installed-vendor
EditMode 175/175 and PlayMode 153/153, no-vendor 175/175 and 138/138; see Testing.md.

Stage 11 incorporates the accepted text-localization subitem, unified scaling/safe
content, transient device previews, licensed WenKai TMP glyphs/fallback, localized
font/sprite/material resources, typed themes and accessibility/formatting extensions.
Node 31/31; full installed-vendor 195 Edit / 170 Play; no-vendor 195 Edit / 155 Play.
See [Localization.md](Localization.md), [Presentation.md](Presentation.md) and Testing.md.

Stage 12 adds scene-owned UI/task cleanup, reference-counted Loading, bounded Toast,
typed confirmation and global system policies, plus unified scene/modal Back input
and a runnable Config-driven sample. Node 32/32; full installed-vendor
212 Edit / 201 Play; no-vendor 212 Edit / 186 Play.
See [SystemUI.md](SystemUI.md) and [Testing.md](Testing.md).

Stage 13 adds opt-in correlated runtime diagnostics, exact owner/duplicate/timeout
inspection, actual ProfilerRecorder sampling and a gated runnable Debug panel.
Full installed-vendor 220 Edit / 214 Play; no-vendor 220 Edit / 199 Play.
Mobile budgets are targets, not Editor-derived device certification.
See [Diagnostics.md](Diagnostics.md) and [Testing.md](Testing.md).

Stage 14 adds transactional Editor page authoring, real scoped prefab/resource
checks and locatable reports, configured device previews and explicit graphs,
fail-closed build profiles/CI hooks and official YooAsset bundle reports/history.
Node 33/33 with three-table validation and six-directory generated parity.
Full installed/no-vendor Edit each pass 246 with five Explicit cases separately
verified; Play passes 214/199. Actual D3D11 rendering and a real local Windows
bundle are verified, not mobile performance or production deployment.
See [EditorTooling.md](EditorTooling.md) and [Testing.md](Testing.md).

Stage 15 adds public-only Hotfix.Sample, an AOT downstream code/game adapter,
explicit reflected construction contracts, executable generic witnesses,
sample linker roots and a target/base-player-specific metadata draft.
Actual Android ARM64 IL2CPP High stripping/native/APK build and retained-member
inspection pass; a distinct High-stripped Windows Mono Player executes the
reflected entry, generic Open, Type prewarm and cleanup. Full installed/no-vendor
Edit each pass 252 (five historical stage-14 Explicit skips); refreshed Play
passes 221/206. Real pending-Start destruction first reproduces the continuation
failure, then verifies Task-backed run drain before shutdown and owned cleanup;
High-stripped native builds and Mono execution are refreshed for that fix.
No HybridCLR installation, metadata generation, DLL publishing or device run.
See [HybridCLRBoundary.md](HybridCLRBoundary.md) and [Testing.md](Testing.md).

## Fixed decisions

- Unity 2022.3 LTS, verified with 2022.3.62f2.
- Android and iOS are the first production targets.
- YooAsset 3.x is the only production resource backend.
- Resources remains only for tests and minimal Editor compatibility.
- Addressables support has been removed in stage 5.
- Runtime asynchronous APIs will use UniTask and `CancellationToken`.
- Breaking API changes are allowed behind a temporary Y1 compatibility facade.
- Y2.0 prepares and validates HybridCLR boundaries but does not install HybridCLR.

## Stages

| Stage | Goal | Acceptance gate |
|---:|---|---|
| 0 (complete) | Freeze baseline and establish tests | Existing lifecycle, pool, and navigation behavior is characterized; EditMode and PlayMode suites pass |
| 1 (complete) | Public contracts and testable runtime | Core services can be constructed without global singletons; Y1 facade and Y2 API share one implementation |
| 2 (complete) | Lifecycle state machine | Every legal/illegal transition, cancellation point, failure rollback, and pooled path is deterministic |
| 3 (complete) | Concurrency and navigation transactions | Rapid open/close/navigation cannot duplicate instances or desynchronize the stack |
| 4 (complete) | UIRoot, layers, sorting, and input | Root/EventSystem ownership is explicit; sorting is reusable; modal input and nested locks are correct |
| 5 (complete) | YooAsset ownership model | Shared loads, leases, cancellation, release, preload, multi-package use, and leak reporting pass tests |
| 6 (complete) | Resource-update bootstrap | EditorSimulate/Offline/Host, weak network, fallback, reset, and update failures have deterministic outcomes |
| 7 (complete) | Pooling and memory governance | Preload, capacity, LRU, scopes, low-memory eviction, and lease release are complete |
| 8 (complete) | Interruptible transitions and visibility | Show/hide can be canceled or reversed without stale writes, transform, alpha, input, or navigation residue |
| 9 (complete) | Messaging and MVVM lifecycle | Typed messages, scoped subscriptions, commands, common bindings, cancellation, and pooling are safe |
| 10 (complete) | Commercial virtual list | Licensed SuperScrollView List/Grid, dynamic List sizes, stable-ID changes/anchors, async leases, nested drag, and 10,000-row/no-vendor gates pass; see Virtualization.md and Testing.md |
| 11 (complete) | Mobile adaptation, localization, and themes | Six device geometries, live language/theme/accessibility, real WenKai glyph/raster/bounded fallback, async leases and transient Editor checks verified |
| 12 (complete) | Scene and system UI services | UI scopes, scene cleanup, reference-counted loading, Toast throttling, and Dialog results are stable |
| 13 (complete) | Diagnostics and performance budgets | Runtime state, ownership, operations, leaks, timings, and uGUI cost can be inspected |
| 14 (complete) | Editor tooling and build gates | Generation, prefab/resource/performance checks, previews, and YooAsset reports block invalid builds |
| 15 (complete) | HybridCLR boundary validation | Public-only Hotfix, verified-resource code extension, actual IL2CPP High retained-member/native build, separate stripped Mono execution, generic witnesses and future metadata draft verified |
| 16 (local work delivered; mobile acceptance blocked) | Package and release candidate | UPM, real fresh consumer/Import, migration/docs/full tests and Android High build verified; Android device UI and iOS native/device gates not passed |

## Stage protocol

Each stage follows this order:

1. Preserve the current workspace and user-owned changes.
2. Mark only the current stage in progress.
3. Add regression coverage before changing protected behavior.
4. Implement only the current stage.
5. Update API and migration documentation.
6. Run the smallest relevant suites, then the full stage gate.
7. Publish an acceptance result and stop.

No later-stage implementation is mixed into an earlier stage merely because the affected file is already open.

## Compatibility policy

- Compatibility APIs forward to Y2 services and never maintain separate state.
- New examples use only Y2 APIs.
- Obsolete messages identify the replacement and planned removal version.
- Every intentional behavior change updates tests and the migration matrix.

## HybridCLR boundary

The future code-hot-update bootstrap runs after YooAsset readiness. The implemented
CodeLoading adapter reuses the AOT Bootstrap extension points; Runtime/Bootstrap
never reference Hotfix business or HybridCLR. The dedicated sample compiles and
registers/opens through public APIs. Actual IL2CPP build/stripping inspection is
not a claim that downloaded code executes on stock IL2CPP, unknown generic value
types are covered, or Android/iOS device code-hot-update was tested.

## Out of scope for this roadmap

- Full HybridCLR installation and hot-update DLL publishing.
- Production Windows, WebGL, mini-game, and console certification.
- Hard dependencies on DOTween, dependency-injection frameworks, analytics, or vendor SDKs.
- Full platform screen-reader implementations.
