using System;
using System.Collections.Generic;
using NUnit.Framework;
using YUIFramework.Bootstrap;

namespace YUIFramework.Tests
{
    public sealed class BootstrapCoreEditModeTests
    {
        [Test]
        public void Profile_CopiesPackagesAndNormalizesCdn()
        {
            var source = new List<BootstrapPackageProfile>
            {
                new BootstrapPackageProfile("DefaultPackage"),
            };

            var profile = CreateProfile(packages: source);
            source.Add(new BootstrapPackageProfile("LateMutation"));

            Assert.That(profile.Packages, Has.Count.EqualTo(1));
            Assert.That(profile.PackageName, Is.EqualTo("DefaultPackage"));
            Assert.That(profile.PrimaryCdn.AbsoluteUri, Is.EqualTo("https://cdn.example.com/content/"));
        }

        [Test]
        public void Profile_EqualityIncludesEveryBehavioralSetting()
        {
            var first = CreateProfile();
            var equal = CreateProfile();
            var different = CreateProfile(maximumAttempts: 2);

            Assert.That(first, Is.EqualTo(equal));
            Assert.That(first.GetHashCode(), Is.EqualTo(equal.GetHashCode()));
            Assert.That(first, Is.Not.EqualTo(different));
        }

        [TestCase("")]
        [TestCase(" ")]
        [TestCase("a/b")]
        [TestCase("a\\b")]
        [TestCase(".")]
        [TestCase("..")]
        [TestCase("name.")]
        [TestCase("name:bad")]
        [TestCase("CON")]
        [TestCase("LPT1.txt")]
        public void PackageProfile_RejectsInvalidName(string packageName)
        {
            Assert.Throws<ArgumentException>(() => new BootstrapPackageProfile(packageName));
        }

        [TestCase("../v2")]
        [TestCase("v2/manifest")]
        [TestCase("..")]
        [TestCase("v2:bad")]
        public void Version_RejectsUnsafeFileSegments(string value)
        {
            Assert.Throws<ArgumentException>(
                () => new BootstrapVersion(value, BootstrapEndpoint.Primary, false));
        }

        [Test]
        public void Profile_RejectsMissingOrDuplicatePackages()
        {
            Assert.Throws<ArgumentException>(
                () => CreateProfile(packages: Array.Empty<BootstrapPackageProfile>()));
            Assert.Throws<ArgumentException>(
                () => CreateProfile(
                    packages: new[]
                    {
                        new BootstrapPackageProfile("A"),
                        new BootstrapPackageProfile("A"),
                    }));
        }

        [Test]
        public void Profile_HostRequiresPrimaryCdn()
        {
            Assert.Throws<ArgumentException>(
                () => CreateProfile(primaryCdn: null, useExplicitPrimary: true));
        }

        [TestCase("ftp://cdn.example.com")]
        [TestCase("https://user:secret@cdn.example.com")]
        [TestCase("https://cdn.example.com?token=secret")]
        [TestCase("https://cdn.example.com/#fragment")]
        public void Profile_RejectsUnsafeCdn(string value)
        {
            Assert.Throws<ArgumentException>(
                () => CreateProfile(primaryCdn: new Uri(value), useExplicitPrimary: true));
        }

        [TestCase(0)]
        [TestCase(11)]
        public void Profile_RejectsUnboundedAttempts(int attempts)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CreateProfile(maximumAttempts: attempts));
        }

        [TestCase(0)]
        [TestCase(65)]
        public void Profile_RejectsInvalidDownloadConcurrency(int concurrency)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CreateProfile(downloadConcurrency: concurrency));
        }

        [Test]
        public void Profile_RejectsInvalidTimeoutBackoffAndDiskMargin()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CreateProfile(operationTimeout: TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CreateProfile(
                    initialBackoff: TimeSpan.FromSeconds(2),
                    maximumBackoff: TimeSpan.FromSeconds(1)));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CreateProfile(diskSafetyMarginBytes: -1));
        }

        [Test]
        public void Profile_RejectsInvalidApplicationChannelVersionAndEnums()
        {
            Assert.Throws<ArgumentException>(
                () => CreateProfile(applicationId: " "));
            Assert.Throws<ArgumentException>(
                () => CreateProfile(channel: " "));
            Assert.Throws<ArgumentException>(
                () => CreateProfile(applicationVersion: " "));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CreateProfile(mode: (BootstrapMode)999));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CreateProfile(fallbackPolicy: (BootstrapFallbackPolicy)999));
        }

        [Test]
        public void StateGraph_ContainsCanonicalSuccessPath()
        {
            var path = new[]
            {
                BootstrapState.Idle,
                BootstrapState.InitializingPackage,
                BootstrapState.RequestingVersion,
                BootstrapState.ActivatingManifest,
                BootstrapState.CalculatingDownload,
                BootstrapState.AwaitingConfirmation,
                BootstrapState.CheckingDisk,
                BootstrapState.Downloading,
                BootstrapState.Verifying,
                BootstrapState.ResourcesReady,
                BootstrapState.LoadingCodeExtension,
                BootstrapState.EnteringGame,
                BootstrapState.Completed,
            };

            for (var i = 1; i < path.Length; i++)
            {
                Assert.That(
                    BootstrapStateGraph.CanTransition(path[i - 1], path[i]),
                    Is.True,
                    $"{path[i - 1]} -> {path[i]}");
            }
        }

        [Test]
        public void StateGraph_AllowsNoDownloadAndNoConfirmationBranches()
        {
            Assert.That(
                BootstrapStateGraph.CanTransition(
                    BootstrapState.CalculatingDownload,
                    BootstrapState.ResourcesReady),
                Is.True);
            Assert.That(
                BootstrapStateGraph.CanTransition(
                    BootstrapState.CalculatingDownload,
                    BootstrapState.CheckingDisk),
                Is.True);
        }

        [Test]
        public void StateGraph_RejectsEarlyGameEntry()
        {
            Assert.That(
                BootstrapStateGraph.CanTransition(
                    BootstrapState.CalculatingDownload,
                    BootstrapState.EnteringGame),
                Is.False);
            Assert.Throws<InvalidOperationException>(
                () => BootstrapStateGraph.EnsureTransition(
                    BootstrapState.Verifying,
                    BootstrapState.EnteringGame));
        }

        [Test]
        public void StateGraph_DefinesEveryDeclaredState()
        {
            foreach (BootstrapState state in Enum.GetValues(typeof(BootstrapState)))
            {
                Assert.That(BootstrapStateGraph.GetAllowedTargets(state), Is.Not.Null, state.ToString());
            }
        }

        [TestCase(BootstrapState.InitializingPackage)]
        [TestCase(BootstrapState.Downloading)]
        [TestCase(BootstrapState.LoadingCodeExtension)]
        public void StateGraph_ActiveStateCanFailCancelOrShutdown(BootstrapState state)
        {
            Assert.That(BootstrapStateGraph.CanTransition(state, BootstrapState.Failed), Is.True);
            Assert.That(BootstrapStateGraph.CanTransition(state, BootstrapState.Canceled), Is.True);
            Assert.That(BootstrapStateGraph.CanTransition(state, BootstrapState.ShuttingDown), Is.True);
        }

        [Test]
        public void RunResult_SuccessRequiresCompletedReadyContext()
        {
            var runId = Guid.NewGuid();
            var context = new BootstrapReadyContext(
                runId,
                new[] { new StubPackageHandle() },
                degraded: true,
                usedFallback: true);

            var result = BootstrapRunResult.Succeeded(runId, context, TimeSpan.FromSeconds(1));

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Degraded, Is.True);
            Assert.That(result.UsedFallback, Is.True);
            Assert.That(result.ErrorCode, Is.EqualTo(BootstrapErrorCode.None));
        }

        [Test]
        public void Progress_RejectsOutOfRangeCounters()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new BootstrapProgress(Guid.NewGuid(), BootstrapState.Downloading, 1.1f));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new BootstrapDownloadProgress(2, 1, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new BootstrapDownloadProgress(0, 0, 2, 1));
        }

        private static BootstrapProfile CreateProfile(
            IEnumerable<BootstrapPackageProfile> packages = null,
            BootstrapMode mode = BootstrapMode.Host,
            string applicationId = "com.example.game",
            string channel = "release",
            string applicationVersion = "1.2.3",
            Uri primaryCdn = null,
            bool useExplicitPrimary = false,
            TimeSpan? operationTimeout = null,
            int maximumAttempts = 3,
            TimeSpan? initialBackoff = null,
            TimeSpan? maximumBackoff = null,
            int downloadConcurrency = 8,
            long diskSafetyMarginBytes = 1024,
            BootstrapFallbackPolicy fallbackPolicy = BootstrapFallbackPolicy.VerifiedLocalOrBuiltin)
        {
            return new BootstrapProfile(
                mode,
                packages ?? new[] { new BootstrapPackageProfile("DefaultPackage") },
                applicationId,
                channel,
                applicationVersion,
                useExplicitPrimary
                    ? primaryCdn
                    : primaryCdn ?? new Uri("https://cdn.example.com/content"),
                new Uri("https://fallback.example.com/content"),
                operationTimeout,
                maximumAttempts,
                initialBackoff,
                maximumBackoff,
                downloadConcurrency,
                fallbackPolicy,
                diskSafetyMarginBytes,
                true);
        }

        private sealed class StubPackageHandle : IBootstrapPackageHandle
        {
            public string PackageName => "DefaultPackage";

            public object NativePackage => this;

            public string ActiveVersion => "1";

            public bool IsManifestVerified => true;
        }
    }
}
