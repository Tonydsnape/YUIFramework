# YUIFramework Y2.0 Roadmap

Y2.0 uses one development line and advances through strict stage gates. Only one stage is implemented at a time. Code, tests, documentation, and acceptance must be complete before the next stage begins.

**Progress: stages 0-10 complete (11 of 17 stages). Stage 11 has not started.**

Independent post-stage-10 work: **Config integration complete**. This connects the
Excel toolchain, atomic typed snapshots and existing UI registrations; it does not
renumber the roadmap or begin stage 11. Acceptance: Node 17/17, installed-vendor
EditMode 175/175 and PlayMode 153/153, no-vendor 175/175 and 138/138; see Testing.md.

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
| 11 | Mobile adaptation, localization, and themes | Safe area, aspect ratios, runtime language/theme refresh, fonts, and missing-content checks work |
| 12 | Scene and system UI services | UI scopes, scene cleanup, reference-counted loading, Toast throttling, and Dialog results are stable |
| 13 | Diagnostics and performance budgets | Runtime state, ownership, operations, leaks, timings, and uGUI cost can be inspected |
| 14 | Editor tooling and build gates | Generation, prefab/resource/performance checks, previews, and YooAsset reports block invalid builds |
| 15 | HybridCLR boundary validation | AOT/Hotfix sample boundaries, generic risks, stripping rules, and the DLL-loading extension point are proven |
| 16 | Package and release candidate | UPM layout, samples, migration, docs, tests, and Android/iOS development-build smoke tests pass |

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

The future code-hot-update bootstrap must run after YooAsset has made resources available. The AOT bootstrap exposes an extension point for loading assemblies, but Y2.0 does not bind Runtime to HybridCLR. A dedicated Hotfix sample assembly will prove that business UI can register and open pages using only public AOT contracts.

## Out of scope for this roadmap

- Full HybridCLR installation and hot-update DLL publishing.
- Production Windows, WebGL, mini-game, and console certification.
- Hard dependencies on DOTween, dependency-injection frameworks, analytics, or vendor SDKs.
- Full platform screen-reader implementations.
