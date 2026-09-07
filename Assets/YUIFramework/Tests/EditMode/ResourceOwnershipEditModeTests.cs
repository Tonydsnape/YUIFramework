using System;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace YUIFramework.Tests
{
    /// <summary>
    /// 阶段 5 资源所有权体系中的同步部分：强类型资源键与 package 注册表。
    /// 这些用例不依赖 PlayerLoop，因此放在 EditMode。
    /// </summary>
    public sealed class ResourceOwnershipEditModeTests
    {
        [Test]
        public void ResourceKey_WithSameTriple_IsEqual()
        {
            var a = new UIResourceKey("UI/Pages/Main", typeof(GameObject), "DefaultPackage");
            var b = UIResourceKey.Of<GameObject>("UI/Pages/Main", "DefaultPackage");

            Assert.That(a, Is.EqualTo(b));
            Assert.That(a == b, Is.True);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        }

        [Test]
        public void ResourceKey_DifferentPackageOrType_IsNotEqual()
        {
            var prefabInA = UIResourceKey.Of<GameObject>("UI/Pages/Main", "A");
            var prefabInB = UIResourceKey.Of<GameObject>("UI/Pages/Main", "B");
            var textureInA = UIResourceKey.Of<Texture2D>("UI/Pages/Main", "A");

            Assert.That(prefabInA, Is.Not.EqualTo(prefabInB), "package 不同必须视为不同资源");
            Assert.That(prefabInA, Is.Not.EqualTo(textureInA), "类型不同必须视为不同资源");
            Assert.That(prefabInA != prefabInB, Is.True);
        }

        [Test]
        public void ResourceKey_RejectsEmptyLocationAndNonUnityType()
        {
            Assert.Throws<ArgumentException>(() => new UIResourceKey(" ", typeof(GameObject)));
            Assert.Throws<ArgumentNullException>(() => new UIResourceKey("UI/A", null));
            Assert.Throws<ArgumentException>(() => new UIResourceKey("UI/A", typeof(string)));
        }

        [Test]
        public void ResourceKey_WithPackage_BindsUnresolvedKey()
        {
            var unresolved = UIResourceKey.Of<GameObject>("UI/Pages/Main");
            Assert.That(unresolved.HasExplicitPackage, Is.False);

            var bound = unresolved.WithPackage("DefaultPackage");
            Assert.That(bound.HasExplicitPackage, Is.True);
            Assert.That(bound.PackageName, Is.EqualTo("DefaultPackage"));
            Assert.That(bound.Location, Is.EqualTo(unresolved.Location));
            Assert.That(bound.AssetType, Is.EqualTo(unresolved.AssetType));
        }

        [Test]
        public void Registry_FirstRegisteredBecomesDefault()
        {
            var registry = new UIResourcePackageRegistry();
            var ui = new StubProvider("UIPackage");
            var extra = new StubProvider("ExtraPackage");

            registry.Register(ui);
            registry.Register(extra);

            Assert.That(registry.DefaultPackageName, Is.EqualTo("UIPackage"));
            Assert.That(registry.Resolve(null), Is.SameAs(ui), "空 package 名应解析为默认 package");
            Assert.That(registry.Resolve("ExtraPackage"), Is.SameAs(extra));
            Assert.That(registry.PackageNames, Has.Count.EqualTo(2));
        }

        [Test]
        public void Registry_ExplicitDefaultWins()
        {
            var registry = new UIResourcePackageRegistry();
            registry.Register(new StubProvider("First"));
            var ui = new StubProvider("UIPackage");
            registry.Register(ui, isDefault: true);

            Assert.That(registry.DefaultPackageName, Is.EqualTo("UIPackage"));
            Assert.That(registry.Resolve(null), Is.SameAs(ui));
        }

        [Test]
        public void Registry_DuplicateRegistrationThrows()
        {
            var registry = new UIResourcePackageRegistry();
            registry.Register(new StubProvider("UIPackage"));

            var error = Assert.Throws<UIResourcePackageAlreadyRegisteredException>(
                () => registry.Register(new StubProvider("UIPackage")));
            Assert.That(error.PackageName, Is.EqualTo("UIPackage"));
        }

        [Test]
        public void Registry_UnknownPackageThrowsWithKnownPackagesListed()
        {
            var registry = new UIResourcePackageRegistry();
            registry.Register(new StubProvider("UIPackage"));

            var error = Assert.Throws<UIResourcePackageNotFoundException>(() => registry.Resolve("Missing"));
            Assert.That(error.PackageName, Is.EqualTo("Missing"));
            Assert.That(error.Message, Does.Contain("UIPackage"));
            Assert.That(registry.TryResolve("Missing", out _), Is.False);
        }

        [Test]
        public void Registry_UnregisterReassignsDefault()
        {
            var registry = new UIResourcePackageRegistry();
            registry.Register(new StubProvider("First"));
            registry.Register(new StubProvider("Second"));

            Assert.That(registry.Unregister("First"), Is.True);
            Assert.That(registry.DefaultPackageName, Is.EqualTo("Second"));
            Assert.That(registry.Unregister("First"), Is.False);
        }

        [Test]
        public void Registry_RejectsNullAndUnnamedProviders()
        {
            var registry = new UIResourcePackageRegistry();
            Assert.Throws<ArgumentNullException>(() => registry.Register(null));
            Assert.Throws<ArgumentException>(() => registry.Register(new StubProvider(" ")));
        }

        [Test]
        public void Registry_FreezePreventsProviderReplacement()
        {
            var registry = new UIResourcePackageRegistry();
            registry.Register(new StubProvider("UIPackage"));

            registry.Freeze();

            Assert.That(registry.IsFrozen, Is.True);
            Assert.Throws<InvalidOperationException>(
                () => registry.Unregister("UIPackage"));
            Assert.Throws<InvalidOperationException>(
                () => registry.Register(new StubProvider("ExtraPackage")));
        }

        private sealed class StubProvider : IUIResourceProvider
        {
            public StubProvider(string packageName)
            {
                PackageName = packageName;
            }

            public string PackageName { get; }

            public UniTask<IUINativeAssetHandle> LoadAssetAsync(UIResourceKey key)
            {
                return UniTask.FromResult<IUINativeAssetHandle>(null);
            }

            public UniTask ShutdownAsync() => UniTask.CompletedTask;
        }
    }
}
