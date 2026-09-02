# Y2.0 Testing Baseline

## Test assemblies

- `YUIFramework.Tests.EditMode`: deterministic framework data structures and contracts.
- `YUIFramework.Tests.PlayMode`: GameObject, lifecycle, pooling, UIRoot, and navigation behavior.

## Run from Unity

Open **Window > General > Test Runner**, then run EditMode and PlayMode suites.

## Run from command line

```powershell
$unity = "C:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Unity.exe"

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
