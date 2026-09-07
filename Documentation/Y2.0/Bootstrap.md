# YUIFramework Y2.0 Resource Bootstrap (Stage 6)

Stage 6 replaces the old static "hot update" startup path with an instance-owned,
deterministic **resource bootstrap**. It updates YooAsset resources; it does not load or
publish hot-update code. Code loading is an optional boundary with no HybridCLR dependency.

## Assembly boundary

```text
YUIFramework.Runtime
        |
        v
YUIFramework.Bootstrap
        |
        v
YUIFramework.Bootstrap.YooAsset
        |
        v
YUIFramework.HotUpdate (obsolete forwarding facades only)
```

- `YUIFramework.Bootstrap` owns the profile, state graph, runner and backend contracts. It has
  no YooAsset dependency and is tested with a fake backend.
- `YUIFramework.Bootstrap.YooAsset` owns the YooAsset 3.0.5 adapter, URL resolver,
  `YooAssetResourceProvider`, and ready-context composition.
- `YUIFramework.HotUpdate` retains old MonoScript GUIDs and API names only for migration. Its
  types are `[Obsolete]` and forward to one Bootstrap runtime; they do not own a second state
  machine.

## Immutable profile

`BootstrapProfile` copies and validates all constructor input. Its package collection and
every exposed setting are read-only.

| Setting | Validation/meaning |
|---|---|
| `Mode` | `EditorSimulate`, `Offline`, or `Host` |
| `Packages` | 1-32 unique safe filename segments; first package is the default UI package |
| `ApplicationId`, `Channel`, `ApplicationVersion` | non-empty, bounded, no control characters |
| `PrimaryCdn`, `FallbackCdn` | absolute HTTP(S), normalized trailing slash, no credentials/query/fragment |
| `OperationTimeout` | greater than zero and at most 10 minutes |
| `MaximumAttempts` | 1-10 attempts per selected endpoint |
| retry backoff | non-negative exponential backoff with a validated maximum |
| `DownloadConcurrency` | 1-64; YooAsset applies its own supported cap |
| `FallbackPolicy` | disabled or verified local/built-in only |
| `DiskSafetyMarginBytes` | non-negative, bounded extra free-space requirement |
| `RequireDownloadConfirmation` | gates every non-empty package plan |

CDN files are scoped as:

```text
{cdn}/{applicationId}/{channel}/{applicationVersion}/{platform}/{package}/{file}
```

Every path segment is escaped. Empty and traversal segments are rejected. Backend-supplied
package versions are also validated as safe single filename segments before reaching YooAsset
sandbox paths.

## State graph and readiness gate

The canonical success path is:

```text
Idle
  -> InitializingPackage
  -> RequestingVersion
  -> ActivatingManifest
  -> CalculatingDownload
  -> AwaitingConfirmation       (only when configured and download is non-empty)
  -> CheckingDisk               (only when download is non-empty)
  -> Downloading                (only when download is non-empty)
  -> Verifying                  (only when download is non-empty)
  -> ResourcesReady
  -> LoadingCodeExtension
  -> EnteringGame
  -> Completed
```

`CalculatingDownload` may go directly to `ResourcesReady` for a zero-size plan, or directly
to `CheckingDisk` when confirmation is disabled. A download/integrity failure may return to
`ActivatingManifest` exactly through a verified fallback recovery. Active states may end in
`Canceled`, `Failed`, or `ShuttingDown`. `Resetting` returns to `Idle`.

`BootstrapStateGraph` is the only legal-transition table. The runner checks every transition.
It creates `BootstrapReadyContext` only after all package manifests are verified and every
required download has passed backend verification. `IBootstrapCodeLoader` runs after
`ResourcesReady`; `IBootstrapGameEntry` runs only after the code loader succeeds. Any earlier
failure leaves business entry untouched.

Each run has a new `RunId`. `BootstrapRunResult` is successful only when its final state is
`Completed`, its error code is `None`, and it contains a ready context.

## Mode behavior

- **EditorSimulate:** accepted only in the Unity Editor. YooAsset uses
  `EditorSimulateModeOptions`; no network monitor or CDN is touched.
- **Offline:** YooAsset uses `OfflinePlayModeOptions`, reads the built-in version/manifest,
  and never touches the network monitor or CDN.
- **Host:** YooAsset uses `HostPlayModeOptions`, requests a version, activates the verified
  manifest, calculates a plan, confirms, checks disk, downloads, and verifies.

Host mode covers first install, no update, and an update with missing local bundles. Every
YooAsset operation checks `Status` and reads `Error` on failure. Built-in and sandbox file
systems use `EFileVerifyLevel.High`, so an empty plan is trusted only after local files pass
full verification.

## Retry, timeout, and fallback

Transient-network and timeout failures are retryable. Integrity, cancellation and permanent
configuration/content failures are not. Attempts and exponential backoff are bounded by the
profile. Version requests exhaust the primary endpoint before selecting the fallback CDN.
YooAsset download retries are driven by the runner, with no unbounded internal retry loop.

The operation timeout cancels the backend attempt and returns without waiting forever. The
YooAsset backend tracks any non-cancelable operation it still owns; Reset/Shutdown drains it
before package destruction and faults after a bounded drain timeout instead of racing or
hanging indefinitely. Every later package operation first crosses the same drain fence, so a
late timed-out primary request/manifest can never overwrite a fallback or newer attempt.

Verified fallback is deliberately conservative:

1. the profile must opt in;
2. the backend must prove an already verified local active manifest, or prefetch and validate
   a built-in version/manifest;
3. a local fallback must produce a zero-download verification plan; otherwise built-in
   fallback is tried, and any accepted fallback must require no unavailable remote content;
4. the result is marked `Degraded` and `UsedFallback`;
5. fallback never reports that the latest remote version was activated.

An unverified or incomplete fallback fails. A failed verification can never activate the
failed remote content.

## Concurrency, cancellation, reset, and shutdown

- Concurrent `RunAsync` calls with an equal profile share one underlying run and `RunId`.
- A different profile is rejected with `AlreadyRunningDifferentProfile`.
- Caller cancellation cancels only that caller's wait. It does not cancel the shared run or
  another caller.
- `ResetAsync` cancels the underlying run, waits for it to settle, resets the backend, and
  returns to `Idle`.
- `ShutdownAsync` is single-flight and idempotent. It cancels and waits for a run/reset, then
  shuts down the backend. New runs are rejected.
- A completed instance can run again: the runner resets the backend first and uses a fresh
  `RunId`.

All runner state is instance-owned. The obsolete facade clears every static event/callback on
`SubsystemRegistration`; primary Y2 code has no static bootstrap state.

The compatibility `GameLauncher` also single-flights repeated launch calls, rejects relaunch
while its ready composition is active, marks ready only after callbacks succeed, and shuts
down only a `UIManager` it initialized itself.

## Contracts and observability

| Contract | Responsibility |
|---|---|
| `IBootstrapBackend` | package/version/manifest/plan/download/verify/fallback/reset/shutdown |
| `IBootstrapClock` / `IBootstrapDelay` | deterministic time, timeout and backoff |
| `IBootstrapNetworkMonitor` | Host reachability only |
| `IBootstrapDiskSpace` | available bytes before download |
| `IBootstrapConfirmation` | per-package user/policy confirmation |
| `IBootstrapProgressSink` | structured state/file/byte progress |
| `IBootstrapTelemetrySink` | structured attempts, transitions, fallback and completion |
| `IBootstrapCodeLoader` | optional post-resource code extension; default is no-op |
| `IBootstrapGameEntry` | application composition and first business entry |

Sink exceptions never change the run outcome. Their type and state are retained in
`BootstrapRunner.SinkDiagnostics`. Telemetry never includes endpoint URLs, and backend error
text is URL-redacted before it reaches `BootstrapFailureContext`.
Download callbacks are correlated to the active run and attempt generation, so a detached
timed-out downloader cannot publish stale progress after failure, reset, or retry.

Multi-package disk checks reserve cumulative bytes per `IBootstrapDiskSpace` storage scope;
two package plans on one volume cannot each pass against the same unreserved free-space value.

## YooAsset and UI-resource composition

```csharp
var profile = new BootstrapProfile(
    BootstrapMode.Host,
    new[] { new BootstrapPackageProfile("DefaultPackage") },
    "com.example.game",
    "release",
    Application.version,
    new Uri("https://cdn.example.com/content/"),
    new Uri("https://backup.example.com/content/"));

var runner = new BootstrapRunner(
    new YooAssetBootstrapBackend(),
    gameEntry,
    progress: progressSink,
    telemetry: telemetrySink);

BootstrapRunResult result = await runner.RunAsync(profile, cancellationToken);
```

`YooAssetBootstrapComposition.Create(readyContext)` registers one
`YooAssetResourceProvider` per ready package in a `UIResourceService`. Package order is
preserved and the first package is the default.

Shutdown ownership is ordered:

```text
UIManager/IUIService shutdown
  -> YooAssetBootstrapComposition.ShutdownResourceServiceAsync()
  -> BootstrapRunner.ShutdownAsync()
```

This releases UI leases/asset handles before the backend destroys packages. Bootstrap
backends hold process-wide runtime leases: `YooAssets.Destroy` runs only after the last
bootstrap client removes its packages and no foreign package remains.
Package teardown attempts every owned package, removes successes immediately, retains failures
for a retry, and aggregates cleanup errors.

## Stage boundary

Stage 6 is complete. Stage 7 pooling and memory governance has not started. The stage 6 code
does not add LRU/capacity policy, HybridCLR, DLL publishing, or any other stage 7+ feature.
