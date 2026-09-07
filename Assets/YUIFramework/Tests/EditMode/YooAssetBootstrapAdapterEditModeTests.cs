using System;
using NUnit.Framework;
using YUIFramework.Bootstrap;
using YUIFramework.Bootstrap.YooAsset;

namespace YUIFramework.Tests
{
    public sealed class YooAssetBootstrapAdapterEditModeTests
    {
        [Test]
        public void CdnUrl_IsNormalizedScopedAndEndpointSpecific()
        {
            var profile = new BootstrapProfile(
                BootstrapMode.Host,
                new[] { new BootstrapPackageProfile("DefaultPackage") },
                "com.example/game",
                "release candidate",
                "1.0.0",
                new Uri("https://primary.example.com/content"),
                new Uri("https://fallback.example.com/content"));

            var primary = YooAssetBootstrapUrl.BuildCdnUrl(
                profile,
                "DefaultPackage",
                "Android",
                "bundles/ui bundle.bundle",
                BootstrapEndpoint.Primary);
            var fallback = YooAssetBootstrapUrl.BuildCdnUrl(
                profile,
                "DefaultPackage",
                "Android",
                "bundles/ui bundle.bundle",
                BootstrapEndpoint.Fallback);

            Assert.That(primary, Does.StartWith("https://primary.example.com/content/"));
            Assert.That(fallback, Does.StartWith("https://fallback.example.com/content/"));
            Assert.That(primary, Does.Contain("com.example%2Fgame/release%20candidate/1.0.0/Android/DefaultPackage/"));
            Assert.That(primary, Does.EndWith("bundles/ui%20bundle.bundle"));
        }

        [TestCase("../manifest.bytes")]
        [TestCase("bundles/../manifest.bytes")]
        [TestCase("bundles//manifest.bytes")]
        public void CdnUrl_RejectsPathTraversalAndEmptySegments(string fileName)
        {
            var profile = new BootstrapProfile(
                BootstrapMode.Host,
                new[] { new BootstrapPackageProfile("DefaultPackage") },
                "com.example.game",
                "release",
                "1",
                new Uri("https://primary.example.com"),
                null);

            Assert.Throws<ArgumentException>(
                () => YooAssetBootstrapUrl.BuildCdnUrl(
                    profile,
                    "DefaultPackage",
                    "Android",
                    fileName,
                    BootstrapEndpoint.Primary));
        }
    }
}
