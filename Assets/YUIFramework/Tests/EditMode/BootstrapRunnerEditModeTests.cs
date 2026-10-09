using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using YUIFramework.Bootstrap;
using YUIFramework.Configuration;
using UnityEngine.TestTools;

namespace YUIFramework.Tests
{
    public sealed class BootstrapRunnerEditModeTests
    {
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

        [UnityTest]
        public IEnumerator ConfigIntegration_VerifiedBootstrapRegistersBeforeBusinessAndStopsOnConfigFailure() =>
            AwaitTest(ConfigIntegrationAsync());

        private async Task ConfigIntegrationAsync()
        {
            foreach (var valid in new[] { true, false })
            {
                var fixture = new Fixture();
                fixture.Backend.SetPlan("DefaultPackage", 1, 64);
                var ui = new UIManager();
                var configs = SampleUIConfiguration.Create(
                    valid ? new EditorJsonConfigSource() : new EditorJsonConfigSource("Assets/MissingConfig"),
                    ConfigFormat.Json);
                var businessEntered = false;
                fixture.GameEntry.Handler = async (context, token) =>
                {
                    Assert.That(fixture.Backend.VerifyCalls, Is.EqualTo(1));
                    Assert.That(context.Packages[0].IsManifestVerified, Is.True);
                    await ui.InitializeAsync(new CodeViewLoader(), cancellationToken: token);
                    await ConfigUIStartup.EnterAsync(configs, ui,
                        snapshot => SampleUIConfiguration.Map(snapshot, "bootstrap"), businessToken =>
                        {
                            Assert.That(ui.IsRegistered<SampleHelloPage>(), Is.True);
                            businessEntered = true;
                            return UniTask.CompletedTask;
                        }, token);
                };
                try
                {
                    var result = await fixture.Runner.RunAsync(CreateProfile());
                    Assert.That(result.IsSuccess, Is.EqualTo(valid));
                    Assert.That(businessEntered, Is.EqualTo(valid));
                    Assert.That(ui.IsRegistered<SampleHelloPage>(), Is.EqualTo(valid));
                }
                finally
                {
                    if (ui.IsInitialized) await ui.ShutdownAsync();
                    await configs.ShutdownAsync();
                    await fixture.Runner.ShutdownAsync();
                }
            }
        }

        [UnityTest]
        public IEnumerator EditorSimulate_HasDeterministicSuccessfulPath() =>
            AwaitTest(ThreeModes_HaveDeterministicSuccessfulPathsAsync(BootstrapMode.EditorSimulate));

        [UnityTest]
        public IEnumerator Offline_HasDeterministicSuccessfulPath() =>
            AwaitTest(ThreeModes_HaveDeterministicSuccessfulPathsAsync(BootstrapMode.Offline));

        [UnityTest]
        public IEnumerator Host_HasDeterministicSuccessfulPath() =>
            AwaitTest(ThreeModes_HaveDeterministicSuccessfulPathsAsync(BootstrapMode.Host));

        private async Task ThreeModes_HaveDeterministicSuccessfulPathsAsync(BootstrapMode mode)
        {
            var fixture = new Fixture();
            var result = await fixture.Runner.RunAsync(CreateProfile(mode));

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(fixture.Backend.InitializeCalls, Is.EqualTo(1));
            Assert.That(fixture.Backend.RequestCalls, Is.EqualTo(1));
            Assert.That(fixture.GameEntry.CallCount, Is.EqualTo(1));
            Assert.That(
                fixture.Backend.RequestEndpoints.Single(),
                Is.EqualTo(mode == BootstrapMode.Host ? BootstrapEndpoint.Primary : BootstrapEndpoint.None));
            Assert.That(
                fixture.Network.ReadCount,
                Is.EqualTo(mode == BootstrapMode.Host ? 1 : 0),
                "Offline and EditorSimulate must not inspect or use the network.");
        }

        [UnityTest]
        public IEnumerator EditorSimulate_RejectsPlayerBuild() =>
            AwaitTest(EditorSimulate_RejectsPlayerBuildAsync());

        private async Task EditorSimulate_RejectsPlayerBuildAsync()
        {
            var fixture = new Fixture();
            fixture.Environment.IsEditorValue = false;

            var result = await fixture.Runner.RunAsync(CreateProfile(BootstrapMode.EditorSimulate));

            Assert.That(result.ErrorCode, Is.EqualTo(BootstrapErrorCode.EditorSimulateUnavailable));
            Assert.That(fixture.Backend.InitializeCalls, Is.Zero);
            Assert.That(fixture.GameEntry.CallCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator HostFirstInstall_DownloadsConfirmsChecksDiskAndVerifies() =>
            AwaitTest(HostFirstInstall_DownloadsConfirmsChecksDiskAndVerifiesAsync());

        private async Task HostFirstInstall_DownloadsConfirmsChecksDiskAndVerifiesAsync()
        {
            var fixture = new Fixture();
            fixture.Backend.SetPlan("DefaultPackage", 3, 900);

            var result = await fixture.Runner.RunAsync(CreateProfile());

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(fixture.Confirmation.CallCount, Is.EqualTo(1));
            Assert.That(fixture.Disk.Queries, Is.EqualTo(1));
            Assert.That(fixture.Backend.DownloadCalls, Is.EqualTo(1));
            Assert.That(fixture.Backend.VerifyCalls, Is.EqualTo(1));
            Assert.That(
                fixture.Progress.Items.Any(item =>
                    item.State == BootstrapState.Downloading &&
                    item.CompletedBytes == 900 &&
                    item.TotalBytes == 900),
                Is.True);
        }

        [UnityTest]
        public IEnumerator HostNoUpdate_SkipsConfirmationDiskDownloadAndVerify() =>
            AwaitTest(HostNoUpdate_SkipsConfirmationDiskDownloadAndVerifyAsync());

        private async Task HostNoUpdate_SkipsConfirmationDiskDownloadAndVerifyAsync()
        {
            var fixture = new Fixture();

            var result = await fixture.Runner.RunAsync(CreateProfile());

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(fixture.Confirmation.CallCount, Is.Zero);
            Assert.That(fixture.Disk.Queries, Is.Zero);
            Assert.That(fixture.Backend.DownloadCalls, Is.Zero);
            Assert.That(fixture.Backend.VerifyCalls, Is.Zero);
        }

        [UnityTest]
        public IEnumerator HostExistingInstallWithUpdate_UsesTheFullDownloadPath() =>
            AwaitTest(HostExistingInstallWithUpdate_UsesTheFullDownloadPathAsync());

        private async Task HostExistingInstallWithUpdate_UsesTheFullDownloadPathAsync()
        {
            var fixture = new Fixture();
            fixture.Backend.SetPlan("DefaultPackage", 2, 512);

            var result = await fixture.Runner.RunAsync(CreateProfile());

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(fixture.Backend.DownloadCalls, Is.EqualTo(1));
            Assert.That(fixture.Backend.VerifyCalls, Is.EqualTo(1));
            Assert.That(result.ReadyContext.Packages[0].ActiveVersion, Is.EqualTo("v2"));
        }

        [UnityTest]
        public IEnumerator Retry_PrimaryEventuallySucceedsWithBoundedBackoff() =>
            AwaitTest(Retry_PrimaryEventuallySucceedsWithBoundedBackoffAsync());

        private async Task Retry_PrimaryEventuallySucceedsWithBoundedBackoffAsync()
        {
            var fixture = new Fixture();
            fixture.Backend.EnqueueVersion(
                BootstrapEndpoint.Primary,
                Failure<BootstrapVersion>(BootstrapBackendErrorKind.TransientNetwork, "temporary-1"));
            fixture.Backend.EnqueueVersion(
                BootstrapEndpoint.Primary,
                Failure<BootstrapVersion>(BootstrapBackendErrorKind.TransientNetwork, "temporary-2"));
            fixture.Backend.EnqueueVersion(
                BootstrapEndpoint.Primary,
                Version("v3", BootstrapEndpoint.Primary));

            var result = await fixture.Runner.RunAsync(CreateProfile(maximumAttempts: 3));

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(fixture.Backend.RequestEndpoints, Is.EqualTo(new[]
            {
                BootstrapEndpoint.Primary,
                BootstrapEndpoint.Primary,
                BootstrapEndpoint.Primary,
            }));
            Assert.That(fixture.Delay.Backoffs, Is.EqualTo(new[]
            {
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMilliseconds(200),
            }));
        }

        [UnityTest]
        public IEnumerator Retry_PrimaryExhaustionSwitchesToFallbackCdn() =>
            AwaitTest(Retry_PrimaryExhaustionSwitchesToFallbackCdnAsync());

        private async Task Retry_PrimaryExhaustionSwitchesToFallbackCdnAsync()
        {
            var fixture = new Fixture();
            fixture.Backend.EnqueueVersion(
                BootstrapEndpoint.Primary,
                Failure<BootstrapVersion>(BootstrapBackendErrorKind.TransientNetwork, "primary-1"));
            fixture.Backend.EnqueueVersion(
                BootstrapEndpoint.Primary,
                Failure<BootstrapVersion>(BootstrapBackendErrorKind.TransientNetwork, "primary-2"));
            fixture.Backend.EnqueueVersion(
                BootstrapEndpoint.Fallback,
                Version("fallback-v2", BootstrapEndpoint.Fallback));

            var result = await fixture.Runner.RunAsync(CreateProfile(maximumAttempts: 2));

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(fixture.Backend.RequestEndpoints, Is.EqualTo(new[]
            {
                BootstrapEndpoint.Primary,
                BootstrapEndpoint.Primary,
                BootstrapEndpoint.Fallback,
            }));
        }

        [UnityTest]
        public IEnumerator Retry_BothCdnsFailExplicitly() =>
            AwaitTest(Retry_BothCdnsFailExplicitlyAsync());

        private async Task Retry_BothCdnsFailExplicitlyAsync()
        {
            var fixture = new Fixture();
            fixture.Backend.DefaultVersionFailure = new BootstrapBackendFailure(
                BootstrapBackendErrorKind.TransientNetwork,
                "request-version",
                "https://secret.example.com/path?token=secret");

            var result = await fixture.Runner.RunAsync(
                CreateProfile(maximumAttempts: 2, fallbackPolicy: BootstrapFallbackPolicy.Disabled));

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(BootstrapErrorCode.VersionRequestFailed));
            Assert.That(fixture.Backend.RequestCalls, Is.EqualTo(4));
            Assert.That(result.Failure.Reason, Does.Not.Contain("secret.example.com"));
            Assert.That(result.Failure.Reason, Does.Contain("<redacted-url>"));
            Assert.That(fixture.GameEntry.CallCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Timeout_IsExplicitAndDoesNotRetryPermanentWorkForever() =>
            AwaitTest(Timeout_IsExplicitAndDoesNotRetryPermanentWorkForeverAsync());

        private async Task Timeout_IsExplicitAndDoesNotRetryPermanentWorkForeverAsync()
        {
            var fixture = new Fixture();
            fixture.Backend.BlockRequest = true;
            fixture.Delay.CompleteTimeouts = true;

            var result = await fixture.Runner.RunAsync(
                CreateProfile(maximumAttempts: 1, fallbackPolicy: BootstrapFallbackPolicy.Disabled));

            Assert.That(result.ErrorCode, Is.EqualTo(BootstrapErrorCode.Timeout));
            Assert.That(fixture.Backend.RequestCalls, Is.EqualTo(2));
            Assert.That(fixture.GameEntry.CallCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator TimedOutDownload_CannotPublishStaleProgress() =>
            AwaitTest(TimedOutDownload_CannotPublishStaleProgressAsync());

        private async Task TimedOutDownload_CannotPublishStaleProgressAsync()
        {
            var fixture = new Fixture();
            fixture.Backend.SetPlan("DefaultPackage", 1, 100);
            fixture.Backend.BlockDownload = true;
            fixture.Backend.IgnoreDownloadCancellation = true;
            fixture.Delay.CompleteTimeouts = true;

            var result = await fixture.Runner.RunAsync(
                CreateProfile(
                    maximumAttempts: 1,
                    fallbackPolicy: BootstrapFallbackPolicy.Disabled));
            Assert.That(result.ErrorCode, Is.EqualTo(BootstrapErrorCode.Timeout));
            var progressCount = fixture.Progress.Items.Count;

            fixture.Backend.ReleaseDownload();
            await UniTask.Yield(PlayerLoopTiming.Update);
            await UniTask.Yield(PlayerLoopTiming.Update);

            Assert.That(
                fixture.Progress.Items.Count,
                Is.EqualTo(progressCount),
                "A detached timed-out attempt must not publish after the failed terminal state.");
        }

        [UnityTest]
        public IEnumerator DisconnectedHost_UsesOnlyVerifiedFallbackAndMarksDegraded() =>
            AwaitTest(DisconnectedHost_UsesOnlyVerifiedFallbackAndMarksDegradedAsync());

        private async Task DisconnectedHost_UsesOnlyVerifiedFallbackAndMarksDegradedAsync()
        {
            var fixture = new Fixture();
            fixture.Network.IsReachable = false;
            fixture.Backend.FallbackAvailable = true;

            var result = await fixture.Runner.RunAsync(CreateProfile());

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Degraded, Is.True);
            Assert.That(result.UsedFallback, Is.True);
            Assert.That(fixture.Backend.RequestCalls, Is.Zero);
            Assert.That(
                fixture.Backend.ActivatedEndpoints,
                Is.EqualTo(new[] { BootstrapEndpoint.VerifiedLocal }));
        }

        [UnityTest]
        public IEnumerator DisconnectedHost_WithoutVerifiedFallbackFails() =>
            AwaitTest(DisconnectedHost_WithoutVerifiedFallbackFailsAsync());

        private async Task DisconnectedHost_WithoutVerifiedFallbackFailsAsync()
        {
            var fixture = new Fixture();
            fixture.Network.IsReachable = false;

            var result = await fixture.Runner.RunAsync(CreateProfile());

            Assert.That(result.ErrorCode, Is.EqualTo(BootstrapErrorCode.NetworkUnavailable));
            Assert.That(fixture.GameEntry.CallCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator UnverifiedFallback_IsRejected() =>
            AwaitTest(UnverifiedFallback_IsRejectedAsync());

        private async Task UnverifiedFallback_IsRejectedAsync()
        {
            var fixture = new Fixture();
            fixture.Network.IsReachable = false;
            fixture.Backend.FallbackAvailable = true;
            fixture.Backend.FallbackIsVerified = false;

            var result = await fixture.Runner.RunAsync(CreateProfile());

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(BootstrapErrorCode.NetworkUnavailable));
            Assert.That(fixture.Backend.ActivateCalls, Is.Zero);
        }

        [UnityTest]
        public IEnumerator InitializationFailure_IsExplicitAndGatesBusiness() =>
            AwaitTest(InitializationFailure_IsExplicitAndGatesBusinessAsync());

        private async Task InitializationFailure_IsExplicitAndGatesBusinessAsync()
        {
            var fixture = new Fixture();
            fixture.Backend.InitializeFailure = new BootstrapBackendFailure(
                BootstrapBackendErrorKind.Permanent,
                "initialize",
                "forced-init-failure");

            var result = await fixture.Runner.RunAsync(CreateProfile());

            Assert.That(result.ErrorCode, Is.EqualTo(BootstrapErrorCode.InitializationFailed));
            Assert.That(fixture.Backend.RequestCalls, Is.Zero);
            Assert.That(fixture.GameEntry.CallCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ManifestFailure_UsesVerifiedFallback() =>
            AwaitTest(ManifestFailure_UsesVerifiedFallbackAsync());

        private async Task ManifestFailure_UsesVerifiedFallbackAsync()
        {
            var fixture = new Fixture();
            fixture.Backend.EnqueueActivation(
                Failure<bool>(BootstrapBackendErrorKind.Permanent, "manifest-failure"));
            fixture.Backend.FallbackAvailable = true;

            var result = await fixture.Runner.RunAsync(CreateProfile());

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Degraded, Is.True);
            Assert.That(fixture.Backend.ActivatedEndpoints, Is.EqualTo(new[]
            {
                BootstrapEndpoint.Primary,
                BootstrapEndpoint.VerifiedLocal,
            }));
        }

        [UnityTest]
        public IEnumerator ManifestFailureWithoutFallback_GatesBusiness() =>
            AwaitTest(ManifestFailureWithoutFallback_GatesBusinessAsync());

        private async Task ManifestFailureWithoutFallback_GatesBusinessAsync()
        {
            var fixture = new Fixture();
            fixture.Backend.EnqueueActivation(
                Failure<bool>(BootstrapBackendErrorKind.Permanent, "manifest-failure"));

            var result = await fixture.Runner.RunAsync(
                CreateProfile(fallbackPolicy: BootstrapFallbackPolicy.Disabled));

            Assert.That(result.ErrorCode, Is.EqualTo(BootstrapErrorCode.ManifestActivationFailed));
            Assert.That(fixture.GameEntry.CallCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ConfirmationDeclined_NeverDownloadsOrEntersGame() =>
            AwaitTest(ConfirmationDeclined_NeverDownloadsOrEntersGameAsync());

        private async Task ConfirmationDeclined_NeverDownloadsOrEntersGameAsync()
        {
            var fixture = new Fixture();
            fixture.Backend.SetPlan("DefaultPackage", 1, 100);
            fixture.Confirmation.Accept = false;

            var result = await fixture.Runner.RunAsync(CreateProfile());

            Assert.That(result.ErrorCode, Is.EqualTo(BootstrapErrorCode.ConfirmationDeclined));
            Assert.That(fixture.Backend.DownloadCalls, Is.Zero);
            Assert.That(fixture.GameEntry.CallCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator InsufficientDisk_NeverDownloadsOrEntersGame() =>
            AwaitTest(InsufficientDisk_NeverDownloadsOrEntersGameAsync());

        private async Task InsufficientDisk_NeverDownloadsOrEntersGameAsync()
        {
            var fixture = new Fixture();
            fixture.Backend.SetPlan("DefaultPackage", 1, 100);
            fixture.Disk.AvailableBytes = 1099;

            var result = await fixture.Runner.RunAsync(
                CreateProfile(diskSafetyMarginBytes: 1000));

            Assert.That(result.ErrorCode, Is.EqualTo(BootstrapErrorCode.InsufficientDiskSpace));
            Assert.That(fixture.Backend.DownloadCalls, Is.Zero);
            Assert.That(fixture.GameEntry.CallCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator MultiPackageDiskCheck_ReservesAggregateBytesPerStorageScope() =>
            AwaitTest(MultiPackageDiskCheck_ReservesAggregateBytesPerStorageScopeAsync());

        private async Task MultiPackageDiskCheck_ReservesAggregateBytesPerStorageScopeAsync()
        {
            var fixture = new Fixture();
            fixture.Backend.SetPlan("DefaultPackage", 1, 700);
            fixture.Backend.SetPlan("UIPackage", 1, 700);
            fixture.Disk.AvailableBytes = 1000;
            var profile = CreateProfile(
                packages: new[]
                {
                    new BootstrapPackageProfile("DefaultPackage"),
                    new BootstrapPackageProfile("UIPackage"),
                },
                diskSafetyMarginBytes: 0);

            var result = await fixture.Runner.RunAsync(profile);

            Assert.That(result.ErrorCode, Is.EqualTo(BootstrapErrorCode.InsufficientDiskSpace));
            Assert.That(fixture.Disk.Queries, Is.EqualTo(2));
            Assert.That(fixture.Backend.DownloadCalls, Is.Zero);
        }

        [UnityTest]
        public IEnumerator StructuredProgress_ReportsConfirmationTotals() =>
            AwaitTest(StructuredProgress_ReportsConfirmationTotalsAsync());

        private async Task StructuredProgress_ReportsConfirmationTotalsAsync()
        {
            var fixture = new Fixture();
            fixture.Backend.SetPlan("DefaultPackage", 4, 2048);

            var result = await fixture.Runner.RunAsync(CreateProfile());

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(
                fixture.Progress.Items.Any(item =>
                    item.State == BootstrapState.AwaitingConfirmation &&
                    item.TotalFiles == 4 &&
                    item.TotalBytes == 2048),
                Is.True);
        }

        [UnityTest]
        public IEnumerator DownloadFailure_UsesVerifiedFallbackWithoutClaimingLatest() =>
            AwaitTest(DownloadFailure_UsesVerifiedFallbackWithoutClaimingLatestAsync());

        private async Task DownloadFailure_UsesVerifiedFallbackWithoutClaimingLatestAsync()
        {
            var fixture = new Fixture();
            fixture.Backend.SetPlan("DefaultPackage", 1, 100);
            fixture.Backend.EnqueueDownload(
                Failure<bool>(BootstrapBackendErrorKind.Permanent, "download-broken"));
            fixture.Backend.FallbackAvailable = true;

            var result = await fixture.Runner.RunAsync(CreateProfile());

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Degraded, Is.True);
            Assert.That(result.UsedFallback, Is.True);
            Assert.That(result.ReadyContext.Packages[0].ActiveVersion, Is.EqualTo("verified-local"));
            Assert.That(fixture.Backend.DownloadCalls, Is.EqualTo(1));
            Assert.That(fixture.GameEntry.CallCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator DownloadFailure_WithoutFallbackFailsBeforeBusiness() =>
            AwaitTest(DownloadFailure_WithoutFallbackFailsBeforeBusinessAsync());

        private async Task DownloadFailure_WithoutFallbackFailsBeforeBusinessAsync()
        {
            var fixture = new Fixture();
            fixture.Backend.SetPlan("DefaultPackage", 1, 100);
            fixture.Backend.EnqueueDownload(
                Failure<bool>(BootstrapBackendErrorKind.Permanent, "download-broken"));

            var result = await fixture.Runner.RunAsync(
                CreateProfile(fallbackPolicy: BootstrapFallbackPolicy.Disabled));

            Assert.That(result.ErrorCode, Is.EqualTo(BootstrapErrorCode.DownloadFailed));
            Assert.That(fixture.GameEntry.CallCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator VerificationFailure_CannotActivateUnverifiedContent() =>
            AwaitTest(VerificationFailure_CannotActivateUnverifiedContentAsync());

        private async Task VerificationFailure_CannotActivateUnverifiedContentAsync()
        {
            var fixture = new Fixture();
            fixture.Backend.SetPlan("DefaultPackage", 1, 100);
            fixture.Backend.EnqueueVerify(
                Failure<bool>(BootstrapBackendErrorKind.Integrity, "hash-mismatch"));

            var result = await fixture.Runner.RunAsync(
                CreateProfile(fallbackPolicy: BootstrapFallbackPolicy.Disabled));

            Assert.That(result.ErrorCode, Is.EqualTo(BootstrapErrorCode.VerificationFailed));
            Assert.That(fixture.GameEntry.CallCount, Is.Zero);
            Assert.That(
                fixture.Telemetry.Events.Any(item => item.State == BootstrapState.EnteringGame),
                Is.False);
        }

        [UnityTest]
        public IEnumerator VerificationFailure_CanRecoverToVerifiedLocalManifest() =>
            AwaitTest(VerificationFailure_CanRecoverToVerifiedLocalManifestAsync());

        private async Task VerificationFailure_CanRecoverToVerifiedLocalManifestAsync()
        {
            var fixture = new Fixture();
            fixture.Backend.SetPlan("DefaultPackage", 1, 100);
            fixture.Backend.EnqueueVerify(
                Failure<bool>(BootstrapBackendErrorKind.Integrity, "hash-mismatch"));
            fixture.Backend.FallbackAvailable = true;

            var result = await fixture.Runner.RunAsync(CreateProfile());

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Degraded, Is.True);
            Assert.That(result.ReadyContext.Packages[0].ActiveVersion, Is.EqualTo("verified-local"));
        }

        [UnityTest]
        public IEnumerator CodeLoaderFailure_PreventsGameEntry() =>
            AwaitTest(CodeLoaderFailure_PreventsGameEntryAsync());

        private async Task CodeLoaderFailure_PreventsGameEntryAsync()
        {
            var fixture = new Fixture();
            fixture.CodeLoader.Failure = new InvalidOperationException("code-load-failed");

            var result = await fixture.Runner.RunAsync(CreateProfile());

            Assert.That(result.ErrorCode, Is.EqualTo(BootstrapErrorCode.CodeLoadFailed));
            Assert.That(fixture.CodeLoader.CallCount, Is.EqualTo(1));
            Assert.That(fixture.GameEntry.CallCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator CodeLoaderRunsAfterResourcesReadyAndBeforeGame() =>
            AwaitTest(CodeLoaderRunsAfterResourcesReadyAndBeforeGameAsync());

        private async Task CodeLoaderRunsAfterResourcesReadyAndBeforeGameAsync()
        {
            var order = new List<string>();
            var fixture = new Fixture(order);

            var result = await fixture.Runner.RunAsync(CreateProfile());

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(order, Is.EqualTo(new[] { "code", "game" }));
            var states = fixture.Telemetry.StateChanges;
            Assert.That(
                states.IndexOf(BootstrapState.ResourcesReady),
                Is.LessThan(states.IndexOf(BootstrapState.LoadingCodeExtension)));
            Assert.That(
                states.IndexOf(BootstrapState.LoadingCodeExtension),
                Is.LessThan(states.IndexOf(BootstrapState.EnteringGame)));
        }

        [UnityTest]
        public IEnumerator GameEntryFailure_IsExplicit() =>
            AwaitTest(GameEntryFailure_IsExplicitAsync());

        private async Task GameEntryFailure_IsExplicitAsync()
        {
            var fixture = new Fixture();
            fixture.GameEntry.Failure = new InvalidOperationException("entry-failure");

            var result = await fixture.Runner.RunAsync(CreateProfile());

            Assert.That(result.ErrorCode, Is.EqualTo(BootstrapErrorCode.EnterGameFailed));
            Assert.That(result.FinalState, Is.EqualTo(BootstrapState.Failed));
        }

        [UnityTest]
        public IEnumerator ThrowingTelemetryAndProgressSinks_DoNotBreakRun() =>
            AwaitTest(ThrowingTelemetryAndProgressSinks_DoNotBreakRunAsync());

        private async Task ThrowingTelemetryAndProgressSinks_DoNotBreakRunAsync()
        {
            var fixture = new Fixture(throwingSinks: true);
            fixture.Backend.SetPlan("DefaultPackage", 1, 100);

            var result = await fixture.Runner.RunAsync(CreateProfile());

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(fixture.Runner.SinkDiagnostics, Is.Not.Empty);
            Assert.That(
                fixture.Runner.SinkDiagnostics.All(item =>
                    string.IsNullOrEmpty(item.ExceptionType) == false),
                Is.True);
        }

        [UnityTest]
        public IEnumerator Telemetry_IsStructuredAndCorrelatedByRunId() =>
            AwaitTest(Telemetry_IsStructuredAndCorrelatedByRunIdAsync());

        private async Task Telemetry_IsStructuredAndCorrelatedByRunIdAsync()
        {
            var fixture = new Fixture();

            var result = await fixture.Runner.RunAsync(CreateProfile());

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(fixture.Telemetry.Events, Is.Not.Empty);
            Assert.That(
                fixture.Telemetry.Events.All(item => item.RunId == result.RunId),
                Is.True);
            Assert.That(
                fixture.Telemetry.Events.Last().Kind,
                Is.EqualTo(BootstrapTelemetryKind.RunCompleted));
        }

        [UnityTest]
        public IEnumerator SameProfileConcurrentRun_IsSingleFlight() =>
            AwaitTest(SameProfileConcurrentRun_IsSingleFlightAsync());

        private async Task SameProfileConcurrentRun_IsSingleFlightAsync()
        {
            var fixture = new Fixture();
            fixture.Backend.BlockRequest = true;
            var profile = CreateProfile();

            var first = fixture.Runner.RunAsync(profile).AsTask();
            var second = fixture.Runner.RunAsync(CreateProfile()).AsTask();
            Assert.That(fixture.Backend.RequestCalls, Is.EqualTo(1));

            fixture.Backend.ReleaseRequest();
            var results = await Task.WhenAll(first, second);

            Assert.That(results[0].RunId, Is.EqualTo(results[1].RunId));
            Assert.That(fixture.GameEntry.CallCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator DifferentProfileConcurrentRun_IsRejectedWithoutDisturbingActiveRun() =>
            AwaitTest(DifferentProfileConcurrentRun_IsRejectedWithoutDisturbingActiveRunAsync());

        private async Task DifferentProfileConcurrentRun_IsRejectedWithoutDisturbingActiveRunAsync()
        {
            var fixture = new Fixture();
            fixture.Backend.BlockRequest = true;
            var first = fixture.Runner.RunAsync(CreateProfile()).AsTask();

            var rejected = await fixture.Runner.RunAsync(CreateProfile(channel: "beta"));

            Assert.That(
                rejected.ErrorCode,
                Is.EqualTo(BootstrapErrorCode.AlreadyRunningDifferentProfile));
            fixture.Backend.ReleaseRequest();
            Assert.That((await first).IsSuccess, Is.True);
            Assert.That(fixture.GameEntry.CallCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator CallerCancellation_CancelsOnlyThatWaiter() =>
            AwaitTest(CallerCancellation_CancelsOnlyThatWaiterAsync());

        private async Task CallerCancellation_CancelsOnlyThatWaiterAsync()
        {
            var fixture = new Fixture();
            fixture.Backend.BlockRequest = true;
            var profile = CreateProfile();
            using (var cancellation = new CancellationTokenSource())
            {
                var canceledWaiter = fixture.Runner.RunAsync(profile, cancellation.Token).AsTask();
                var survivor = fixture.Runner.RunAsync(CreateProfile()).AsTask();

                cancellation.Cancel();
                try
                {
                    await canceledWaiter;
                    Assert.Fail("Expected the caller wait to be canceled.");
                }
                catch (OperationCanceledException)
                {
                }

                fixture.Backend.ReleaseRequest();
                var result = await survivor;
                Assert.That(result.IsSuccess, Is.True);
                Assert.That(fixture.GameEntry.CallCount, Is.EqualTo(1));
            }
        }

        [UnityTest]
        public IEnumerator Reset_CancelsAndWaitsForInflightThenReturnsIdle() =>
            AwaitTest(Reset_CancelsAndWaitsForInflightThenReturnsIdleAsync());

        private async Task Reset_CancelsAndWaitsForInflightThenReturnsIdleAsync()
        {
            var fixture = new Fixture();
            fixture.Backend.BlockRequest = true;
            var run = fixture.Runner.RunAsync(CreateProfile()).AsTask();

            await fixture.Runner.ResetAsync();
            var result = await run;

            Assert.That(result.FinalState, Is.EqualTo(BootstrapState.Canceled));
            Assert.That(fixture.Runner.State, Is.EqualTo(BootstrapState.Idle));
            Assert.That(fixture.Backend.ResetCalls, Is.EqualTo(1));
            Assert.That(fixture.GameEntry.CallCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ResetDuringNonCooperativeCodeLoad_NeverEntersBusiness() =>
            AwaitTest(ResetDuringNonCooperativeCodeLoad_NeverEntersBusinessAsync());

        private async Task ResetDuringNonCooperativeCodeLoad_NeverEntersBusinessAsync()
        {
            var fixture = new Fixture();
            fixture.CodeLoader.Block = true;
            var run = fixture.Runner.RunAsync(CreateProfile()).AsTask();
            Assert.That(fixture.CodeLoader.CallCount, Is.EqualTo(1));

            var reset = fixture.Runner.ResetAsync().AsTask();
            Assert.That(reset.IsCompleted, Is.False);
            fixture.CodeLoader.Release();

            await reset;
            var result = await run;
            Assert.That(result.FinalState, Is.EqualTo(BootstrapState.Canceled));
            Assert.That(fixture.GameEntry.CallCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ResetDuringNonCooperativeGameEntry_CannotReportCompleted() =>
            AwaitTest(ResetDuringNonCooperativeGameEntry_CannotReportCompletedAsync());

        private async Task ResetDuringNonCooperativeGameEntry_CannotReportCompletedAsync()
        {
            var fixture = new Fixture();
            fixture.GameEntry.Block = true;
            var run = fixture.Runner.RunAsync(CreateProfile()).AsTask();
            Assert.That(fixture.GameEntry.CallCount, Is.EqualTo(1));

            var reset = fixture.Runner.ResetAsync().AsTask();
            Assert.That(reset.IsCompleted, Is.False);
            fixture.GameEntry.Release();

            await reset;
            var result = await run;
            Assert.That(result.FinalState, Is.EqualTo(BootstrapState.Canceled));
            Assert.That(result.IsSuccess, Is.False);
        }

        [UnityTest]
        public IEnumerator Shutdown_CancelsInflightIsIdempotentAndRejectsNewRuns() =>
            AwaitTest(Shutdown_CancelsInflightIsIdempotentAndRejectsNewRunsAsync());

        private async Task Shutdown_CancelsInflightIsIdempotentAndRejectsNewRunsAsync()
        {
            var fixture = new Fixture();
            fixture.Backend.BlockRequest = true;
            var run = fixture.Runner.RunAsync(CreateProfile()).AsTask();

            await Task.WhenAll(
                fixture.Runner.ShutdownAsync().AsTask(),
                fixture.Runner.ShutdownAsync().AsTask());
            var canceled = await run;
            var rejected = await fixture.Runner.RunAsync(CreateProfile());

            Assert.That(canceled.FinalState, Is.EqualTo(BootstrapState.Canceled));
            Assert.That(fixture.Runner.State, Is.EqualTo(BootstrapState.ShuttingDown));
            Assert.That(fixture.Backend.ShutdownCalls, Is.EqualTo(1));
            Assert.That(rejected.ErrorCode, Is.EqualTo(BootstrapErrorCode.RunnerShutDown));
        }

        [UnityTest]
        public IEnumerator ResetDuringShutdown_JoinsShutdownWithoutBackendRace() =>
            AwaitTest(ResetDuringShutdown_JoinsShutdownWithoutBackendRaceAsync());

        private async Task ResetDuringShutdown_JoinsShutdownWithoutBackendRaceAsync()
        {
            var fixture = new Fixture();
            fixture.Backend.BlockRequest = true;
            var run = fixture.Runner.RunAsync(CreateProfile()).AsTask();

            var shutdown = fixture.Runner.ShutdownAsync().AsTask();
            var reset = fixture.Runner.ResetAsync().AsTask();
            await Task.WhenAll(shutdown, reset);
            await run;

            Assert.That(fixture.Backend.ResetCalls, Is.Zero);
            Assert.That(fixture.Backend.ShutdownCalls, Is.EqualTo(1));
            Assert.That(fixture.Runner.State, Is.EqualTo(BootstrapState.ShuttingDown));
        }

        [UnityTest]
        public IEnumerator ResetFailure_DoesNotPreventBackendShutdown() =>
            AwaitTest(ResetFailure_DoesNotPreventBackendShutdownAsync());

        private async Task ResetFailure_DoesNotPreventBackendShutdownAsync()
        {
            var fixture = new Fixture();
            fixture.Backend.BlockReset = true;
            fixture.Backend.ResetFailure = new InvalidOperationException("forced-reset-failure");

            var reset = fixture.Runner.ResetAsync().AsTask();
            var shutdown = fixture.Runner.ShutdownAsync().AsTask();
            fixture.Backend.ReleaseReset();

            try
            {
                await reset;
                Assert.Fail("Expected reset to fail.");
            }
            catch (InvalidOperationException)
            {
            }

            try
            {
                await shutdown;
                Assert.Fail("Expected shutdown to report the reset failure.");
            }
            catch (AggregateException)
            {
            }

            Assert.That(fixture.Backend.ShutdownCalls, Is.EqualTo(1));
            Assert.That(fixture.Runner.State, Is.EqualTo(BootstrapState.ShuttingDown));
        }

        [UnityTest]
        public IEnumerator ConsecutiveRuns_ResetBackendAndUseFreshRunIds() =>
            AwaitTest(ConsecutiveRuns_ResetBackendAndUseFreshRunIdsAsync());

        private async Task ConsecutiveRuns_ResetBackendAndUseFreshRunIdsAsync()
        {
            var fixture = new Fixture();

            var first = await fixture.Runner.RunAsync(CreateProfile());
            var second = await fixture.Runner.RunAsync(CreateProfile());

            Assert.That(first.IsSuccess, Is.True);
            Assert.That(second.IsSuccess, Is.True);
            Assert.That(second.RunId, Is.Not.EqualTo(first.RunId));
            Assert.That(fixture.Backend.ResetCalls, Is.EqualTo(1));
            Assert.That(fixture.GameEntry.CallCount, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator MultiplePackages_AreDeliveredInReadyContext() =>
            AwaitTest(MultiplePackages_AreDeliveredInReadyContextAsync());

        private async Task MultiplePackages_AreDeliveredInReadyContextAsync()
        {
            var fixture = new Fixture();
            var profile = CreateProfile(
                packages: new[]
                {
                    new BootstrapPackageProfile("DefaultPackage"),
                    new BootstrapPackageProfile("UIPackage"),
                });

            var result = await fixture.Runner.RunAsync(profile);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(
                result.ReadyContext.Packages.Select(item => item.PackageName),
                Is.EqualTo(new[] { "DefaultPackage", "UIPackage" }));
            Assert.That(fixture.Backend.InitializeCalls, Is.EqualTo(2));
            Assert.That(fixture.Backend.RequestCalls, Is.EqualTo(2));
        }

        private static IEnumerator AwaitTest(Task task)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsCanceled)
            {
                throw new TaskCanceledException(task);
            }

            if (task.IsFaulted)
            {
                throw task.Exception?.GetBaseException() ??
                      new InvalidOperationException("Bootstrap test task failed.");
            }
        }

        private static BootstrapProfile CreateProfile(
            BootstrapMode mode = BootstrapMode.Host,
            IEnumerable<BootstrapPackageProfile> packages = null,
            string channel = "release",
            int maximumAttempts = 3,
            BootstrapFallbackPolicy fallbackPolicy = BootstrapFallbackPolicy.VerifiedLocalOrBuiltin,
            long diskSafetyMarginBytes = 1000)
        {
            return new BootstrapProfile(
                mode,
                packages ?? new[] { new BootstrapPackageProfile("DefaultPackage") },
                "com.example.game",
                channel,
                "1.0.0",
                new Uri("https://primary.example.com/content"),
                new Uri("https://fallback.example.com/content"),
                TestTimeout,
                maximumAttempts,
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMilliseconds(250),
                4,
                fallbackPolicy,
                diskSafetyMarginBytes,
                true);
        }

        private static BootstrapBackendResult<BootstrapVersion> Version(
            string version,
            BootstrapEndpoint endpoint)
        {
            return BootstrapBackendResult<BootstrapVersion>.Succeeded(
                new BootstrapVersion(version, endpoint, false));
        }

        private static BootstrapBackendResult<T> Failure<T>(
            BootstrapBackendErrorKind kind,
            string reason)
        {
            return BootstrapBackendResult<T>.Failed(
                new BootstrapBackendFailure(kind, "fake", reason));
        }

        private sealed class Fixture
        {
            public Fixture(List<string> order = null, bool throwingSinks = false)
            {
                Backend = new FakeBackend();
                Clock = new FakeClock();
                Delay = new FakeDelay(Clock, TestTimeout);
                Disk = new FakeDisk();
                Confirmation = new FakeConfirmation();
                Network = new FakeNetwork();
                Environment = new FakeEnvironment();
                CodeLoader = new FakeCodeLoader(order);
                GameEntry = new FakeGameEntry(order);
                Telemetry = new CollectingTelemetry(throwingSinks);
                Progress = new CollectingProgress(throwingSinks);
                Runner = new BootstrapRunner(
                    Backend,
                    GameEntry,
                    Telemetry,
                    Progress,
                    CodeLoader,
                    Clock,
                    Delay,
                    Disk,
                    Confirmation,
                    Network,
                    Environment);
            }

            public FakeBackend Backend { get; }
            public FakeClock Clock { get; }
            public FakeDelay Delay { get; }
            public FakeDisk Disk { get; }
            public FakeConfirmation Confirmation { get; }
            public FakeNetwork Network { get; }
            public FakeEnvironment Environment { get; }
            public FakeCodeLoader CodeLoader { get; }
            public FakeGameEntry GameEntry { get; }
            public CollectingTelemetry Telemetry { get; }
            public CollectingProgress Progress { get; }
            public BootstrapRunner Runner { get; }
        }

        private sealed class FakePackageHandle : IBootstrapPackageHandle
        {
            public FakePackageHandle(string packageName)
            {
                PackageName = packageName;
            }

            public string PackageName { get; }
            public object NativePackage => this;
            public string ActiveVersion { get; set; }
            public bool IsManifestVerified { get; set; }
            public BootstrapEndpoint ActiveEndpoint { get; set; }
        }

        private sealed class FakeBackend : IBootstrapBackend
        {
            private readonly Dictionary<BootstrapEndpoint, Queue<BootstrapBackendResult<BootstrapVersion>>>
                _versions =
                    new Dictionary<BootstrapEndpoint, Queue<BootstrapBackendResult<BootstrapVersion>>>();
            private readonly Queue<BootstrapBackendResult<bool>> _downloads =
                new Queue<BootstrapBackendResult<bool>>();
                private readonly Queue<BootstrapBackendResult<bool>> _activations =
                    new Queue<BootstrapBackendResult<bool>>();
            private readonly Queue<BootstrapBackendResult<bool>> _verifications =
                new Queue<BootstrapBackendResult<bool>>();
            private readonly Dictionary<string, Tuple<int, long>> _plans =
                new Dictionary<string, Tuple<int, long>>(StringComparer.Ordinal);
            private TaskCompletionSource<object> _requestGate = NewGate();
            private TaskCompletionSource<object> _resetGate = NewGate();
            private TaskCompletionSource<object> _downloadGate = NewGate();

            public int InitializeCalls { get; private set; }
            public int RequestCalls { get; private set; }
            public int ActivateCalls { get; private set; }
            public int DownloadCalls { get; private set; }
            public int VerifyCalls { get; private set; }
            public int ResetCalls { get; private set; }
            public int ShutdownCalls { get; private set; }
            public bool BlockRequest { get; set; }
            public bool FallbackAvailable { get; set; }
            public bool FallbackIsVerified { get; set; } = true;
            public BootstrapBackendFailure InitializeFailure { get; set; }
            public bool BlockReset { get; set; }
            public Exception ResetFailure { get; set; }
            public bool BlockDownload { get; set; }
            public bool IgnoreDownloadCancellation { get; set; }
            public BootstrapBackendFailure DefaultVersionFailure { get; set; }
            public List<BootstrapEndpoint> RequestEndpoints { get; } = new List<BootstrapEndpoint>();
            public List<BootstrapEndpoint> ActivatedEndpoints { get; } = new List<BootstrapEndpoint>();

            public void SetPlan(string packageName, int files, long bytes)
            {
                _plans[packageName] = Tuple.Create(files, bytes);
            }

            public void EnqueueVersion(
                BootstrapEndpoint endpoint,
                BootstrapBackendResult<BootstrapVersion> result)
            {
                if (!_versions.TryGetValue(endpoint, out var queue))
                {
                    queue = new Queue<BootstrapBackendResult<BootstrapVersion>>();
                    _versions.Add(endpoint, queue);
                }

                queue.Enqueue(result);
            }

            public void EnqueueDownload(BootstrapBackendResult<bool> result)
            {
                _downloads.Enqueue(result);
            }

            public void EnqueueActivation(BootstrapBackendResult<bool> result)
            {
                _activations.Enqueue(result);
            }

            public void EnqueueVerify(BootstrapBackendResult<bool> result)
            {
                _verifications.Enqueue(result);
            }

            public void ReleaseRequest()
            {
                BlockRequest = false;
                _requestGate.TrySetResult(null);
            }

            public void ReleaseReset()
            {
                BlockReset = false;
                _resetGate.TrySetResult(null);
            }

            public void ReleaseDownload()
            {
                BlockDownload = false;
                _downloadGate.TrySetResult(null);
            }

            public UniTask<BootstrapBackendResult<IBootstrapPackageHandle>> InitializePackageAsync(
                BootstrapPackageProfile package,
                BootstrapProfile profile,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                InitializeCalls++;
                if (InitializeFailure != null)
                {
                    return UniTask.FromResult(
                        BootstrapBackendResult<IBootstrapPackageHandle>.Failed(
                            InitializeFailure));
                }

                return UniTask.FromResult(
                    BootstrapBackendResult<IBootstrapPackageHandle>.Succeeded(
                        new FakePackageHandle(package.PackageName)));
            }

            public async UniTask<BootstrapBackendResult<BootstrapVersion>> RequestVersionAsync(
                IBootstrapPackageHandle package,
                BootstrapProfile profile,
                BootstrapEndpoint endpoint,
                CancellationToken cancellationToken)
            {
                RequestCalls++;
                RequestEndpoints.Add(endpoint);
                if (BlockRequest)
                {
                    await _requestGate.Task
                        .AsUniTask()
                        .AttachExternalCancellation(cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (_versions.TryGetValue(endpoint, out var queue) && queue.Count > 0)
                {
                    return queue.Dequeue();
                }

                if (DefaultVersionFailure != null)
                {
                    return BootstrapBackendResult<BootstrapVersion>.Failed(DefaultVersionFailure);
                }

                var source = endpoint;
                var verified = false;
                if (profile.Mode == BootstrapMode.EditorSimulate)
                {
                    source = BootstrapEndpoint.VerifiedLocal;
                    verified = true;
                }
                else if (profile.Mode == BootstrapMode.Offline)
                {
                    source = BootstrapEndpoint.VerifiedBuiltin;
                    verified = true;
                }

                return BootstrapBackendResult<BootstrapVersion>.Succeeded(
                    new BootstrapVersion("v2", source, verified));
            }

            public UniTask<BootstrapBackendResult<bool>> ActivateManifestAsync(
                IBootstrapPackageHandle package,
                BootstrapVersion version,
                BootstrapProfile profile,
                BootstrapEndpoint endpoint,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ActivateCalls++;
                ActivatedEndpoints.Add(endpoint);
                if (_activations.Count > 0)
                {
                    var scripted = _activations.Dequeue();
                    if (!scripted.IsSuccess || !scripted.Value)
                    {
                        return UniTask.FromResult(scripted);
                    }
                }

                var handle = (FakePackageHandle)package;
                handle.ActiveVersion = version.Value;
                handle.ActiveEndpoint = endpoint;
                handle.IsManifestVerified = true;
                return UniTask.FromResult(BootstrapBackendResult<bool>.Succeeded(true));
            }

            public UniTask<BootstrapBackendResult<BootstrapDownloadPlan>> CalculateDownloadAsync(
                IBootstrapPackageHandle package,
                BootstrapProfile profile,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var handle = (FakePackageHandle)package;
                var files = 0;
                long bytes = 0;
                if (handle.ActiveEndpoint != BootstrapEndpoint.VerifiedLocal &&
                    handle.ActiveEndpoint != BootstrapEndpoint.VerifiedBuiltin &&
                    _plans.TryGetValue(package.PackageName, out var plan))
                {
                    files = plan.Item1;
                    bytes = plan.Item2;
                }

                return UniTask.FromResult(
                    BootstrapBackendResult<BootstrapDownloadPlan>.Succeeded(
                        new BootstrapDownloadPlan(package, files, bytes, package.PackageName)));
            }

            public async UniTask<BootstrapBackendResult<bool>> DownloadAsync(
                BootstrapDownloadPlan plan,
                Action<BootstrapDownloadProgress> progress,
                CancellationToken cancellationToken)
            {
                if (!IgnoreDownloadCancellation)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                DownloadCalls++;
                if (BlockDownload)
                {
                    await _downloadGate.Task;
                }

                if (!IgnoreDownloadCancellation)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (_downloads.Count > 0)
                {
                    var scripted = _downloads.Dequeue();
                    if (!scripted.IsSuccess)
                    {
                        return scripted;
                    }
                }

                progress?.Invoke(new BootstrapDownloadProgress(
                    plan.FileCount,
                    plan.FileCount,
                    plan.TotalBytes,
                    plan.TotalBytes));
                return BootstrapBackendResult<bool>.Succeeded(true);
            }

            public UniTask<BootstrapBackendResult<bool>> VerifyAsync(
                BootstrapDownloadPlan plan,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                VerifyCalls++;
                return UniTask.FromResult(
                    _verifications.Count > 0
                        ? _verifications.Dequeue()
                        : BootstrapBackendResult<bool>.Succeeded(true));
            }

            public UniTask<BootstrapBackendResult<BootstrapVersion>> FindVerifiedFallbackAsync(
                IBootstrapPackageHandle package,
                BootstrapProfile profile,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return UniTask.FromResult(
                    FallbackAvailable
                        ? BootstrapBackendResult<BootstrapVersion>.Succeeded(
                            new BootstrapVersion(
                                "verified-local",
                                BootstrapEndpoint.VerifiedLocal,
                                FallbackIsVerified))
                        : Failure<BootstrapVersion>(
                            BootstrapBackendErrorKind.Permanent,
                            "no-verified-fallback"));
            }

            public async UniTask ResetAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ResetCalls++;
                if (BlockReset)
                {
                    await _resetGate.Task;
                }

                if (ResetFailure != null)
                {
                    throw ResetFailure;
                }

                _requestGate = NewGate();
                _resetGate = NewGate();
                _downloadGate = NewGate();
            }

            public UniTask ShutdownAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ShutdownCalls++;
                return UniTask.CompletedTask;
            }

            private static TaskCompletionSource<object> NewGate()
            {
                return new TaskCompletionSource<object>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        private sealed class FakeClock : IBootstrapClock
        {
            public DateTimeOffset UtcNow { get; private set; } =
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

            public void Advance(TimeSpan value)
            {
                UtcNow += value;
            }
        }

        private sealed class FakeDelay : IBootstrapDelay
        {
            private readonly FakeClock _clock;
            private readonly TimeSpan _timeout;

            public FakeDelay(FakeClock clock, TimeSpan timeout)
            {
                _clock = clock;
                _timeout = timeout;
            }

            public bool CompleteTimeouts { get; set; }
            public List<TimeSpan> Backoffs { get; } = new List<TimeSpan>();

            public UniTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (delay == _timeout)
                {
                    if (CompleteTimeouts)
                    {
                        _clock.Advance(delay);
                        return UniTask.CompletedTask;
                    }

                    return WaitForCancellationAsync(cancellationToken);
                }

                Backoffs.Add(delay);
                _clock.Advance(delay);
                return UniTask.CompletedTask;
            }

            private static async UniTask WaitForCancellationAsync(CancellationToken cancellationToken)
            {
                var source = new UniTaskCompletionSource();
                using (cancellationToken.Register(() => source.TrySetCanceled()))
                {
                    await source.Task;
                }
            }
        }

        private sealed class FakeDisk : IBootstrapDiskSpace
        {
            public long AvailableBytes { get; set; } = long.MaxValue;
            public int Queries { get; private set; }
            public string StorageScope { get; set; } = "shared";

            public string GetStorageScope(string packageName)
            {
                return StorageScope;
            }

            public long GetAvailableBytes(string packageName)
            {
                Queries++;
                return AvailableBytes;
            }
        }

        private sealed class FakeConfirmation : IBootstrapConfirmation
        {
            public bool Accept { get; set; } = true;
            public int CallCount { get; private set; }

            public UniTask<bool> ConfirmAsync(
                BootstrapDownloadPlan plan,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CallCount++;
                return UniTask.FromResult(Accept);
            }
        }

        private sealed class FakeNetwork : IBootstrapNetworkMonitor
        {
            public bool IsReachable { get; set; } = true;
            public int ReadCount { get; private set; }

            public bool IsNetworkReachable
            {
                get
                {
                    ReadCount++;
                    return IsReachable;
                }
            }
        }

        private sealed class FakeEnvironment : IBootstrapRuntimeEnvironment
        {
            public bool IsEditorValue { get; set; } = true;
            public bool IsEditor => IsEditorValue;
            public string PlatformName => "Test";
        }

        private sealed class FakeCodeLoader : IBootstrapCodeLoader
        {
            private readonly IList<string> _order;
            private readonly TaskCompletionSource<object> _gate =
                new TaskCompletionSource<object>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

            public FakeCodeLoader(IList<string> order)
            {
                _order = order;
            }

            public Exception Failure { get; set; }
            public int CallCount { get; private set; }
            public bool Block { get; set; }

            public async UniTask LoadAsync(
                BootstrapReadyContext context,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CallCount++;
                _order?.Add("code");
                if (Failure != null)
                {
                    throw Failure;
                }

                if (Block)
                {
                    await _gate.Task;
                }
            }

            public void Release()
            {
                _gate.TrySetResult(null);
            }
        }

        private sealed class FakeGameEntry : IBootstrapGameEntry
        {
            private readonly IList<string> _order;
            private readonly TaskCompletionSource<object> _gate =
                new TaskCompletionSource<object>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

            public FakeGameEntry(IList<string> order)
            {
                _order = order;
            }

            public int CallCount { get; private set; }
            public Exception Failure { get; set; }
            public bool Block { get; set; }
            public Func<BootstrapReadyContext, CancellationToken, UniTask> Handler { get; set; }

            public async UniTask EnterAsync(
                BootstrapReadyContext context,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CallCount++;
                _order?.Add("game");
                if (Failure != null)
                {
                    throw Failure;
                }

                if (Block)
                {
                    await _gate.Task;
                }
                if (Handler != null) await Handler(context, cancellationToken);
            }

            public void Release()
            {
                _gate.TrySetResult(null);
            }
        }

        private sealed class CollectingTelemetry : IBootstrapTelemetrySink
        {
            private readonly bool _throws;

            public CollectingTelemetry(bool throws)
            {
                _throws = throws;
            }

            public List<BootstrapTelemetryEvent> Events { get; } =
                new List<BootstrapTelemetryEvent>();

            public List<BootstrapState> StateChanges =>
                Events
                    .Where(item => item.Kind == BootstrapTelemetryKind.StateChanged)
                    .Select(item => item.State)
                    .ToList();

            public void Record(BootstrapTelemetryEvent telemetryEvent)
            {
                Events.Add(telemetryEvent);
                if (_throws)
                {
                    throw new InvalidOperationException("telemetry-sink-failure");
                }
            }
        }

        private sealed class CollectingProgress : IBootstrapProgressSink
        {
            private readonly bool _throws;

            public CollectingProgress(bool throws)
            {
                _throws = throws;
            }

            public List<BootstrapProgress> Items { get; } = new List<BootstrapProgress>();

            public void Report(BootstrapProgress progress)
            {
                Items.Add(progress);
                if (_throws)
                {
                    throw new InvalidOperationException("progress-sink-failure");
                }
            }
        }
    }
}
