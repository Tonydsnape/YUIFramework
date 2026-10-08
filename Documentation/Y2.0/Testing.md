# Y2.0 Testing Baseline

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
