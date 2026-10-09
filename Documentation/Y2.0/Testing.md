# Y2.0 Testing Baseline

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
