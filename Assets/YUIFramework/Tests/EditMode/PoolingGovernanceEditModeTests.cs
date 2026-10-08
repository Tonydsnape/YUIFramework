using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace YUIFramework.Tests
{
    public sealed class PoolingGovernanceEditModeTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var item in _objects)
            {
                if (item != null)
                {
                    UnityEngine.Object.DestroyImmediate(item);
                }
            }

            _objects.Clear();
        }

        [Test]
        public void Return_DuplicateAndCrossPool_AreRejectedWithDiagnostics()
        {
            var firstPool = new UIObjectPool();
            var secondPool = new UIObjectPool();
            var entry = CreateEntry("owned");
            var policy = new UIPoolPolicy(true, 2);

            Assert.That(
                firstPool.TryRelease(
                    typeof(TestContext),
                    entry,
                    policy,
                    UIPoolScope.Global,
                    "Open",
                    true,
                    out _,
                    out var accepted),
                Is.True);
            Assert.That(accepted, Is.EqualTo(UIPoolReturnRejection.None));

            Assert.That(
                firstPool.TryRelease(
                    typeof(TestContext),
                    entry,
                    policy,
                    UIPoolScope.Global,
                    "Open",
                    true,
                    out _,
                    out var duplicate),
                Is.False);
            Assert.That(duplicate, Is.EqualTo(UIPoolReturnRejection.DuplicateReturn));

            Assert.That(firstPool.TryGet(typeof(TestContext), out var borrowed), Is.True);
            Assert.That(
                secondPool.TryRelease(
                    typeof(TestContext),
                    borrowed,
                    policy,
                    UIPoolScope.Global,
                    "Open",
                    true,
                    out _,
                    out var crossPool),
                Is.False);
            Assert.That(crossPool, Is.EqualTo(UIPoolReturnRejection.CrossPoolReturn));
            Assert.That(firstPool.GetDiagnostics().RejectedReturnCount, Is.EqualTo(1));
            Assert.That(secondPool.GetDiagnostics().RejectedReturnCount, Is.EqualTo(1));
        }

        [Test]
        public void CapacityRejection_DoesNotClaimPreviouslyUnownedEntry()
        {
            var fullPool = new UIObjectPool(globalCapacity: 1);
            var acceptingPool = new UIObjectPool(globalCapacity: 1);
            var resident = CreateEntry("resident");
            var rejected = CreateEntry("rejected");
            var policy = new UIPoolPolicy(true, 1);
            var rejectedPolicy = new UIPoolPolicy(true, 1, priority: -1);
            Return(fullPool, resident, policy);

            Assert.That(
                fullPool.TryRelease(
                    typeof(TestContext),
                    rejected,
                    rejectedPolicy,
                    UIPoolScope.Global,
                    "Open",
                    false,
                    out var overflow,
                    out var rejection),
                Is.False);
            Assert.That(overflow, Is.SameAs(rejected));
            Assert.That(rejection, Is.EqualTo(UIPoolReturnRejection.Capacity));

            Assert.That(
                acceptingPool.TryRelease(
                    typeof(TestContext),
                    rejected,
                    rejectedPolicy,
                    UIPoolScope.Global,
                    "Open",
                    false,
                    out _,
                    out var accepted),
                Is.True);
            Assert.That(accepted, Is.EqualTo(UIPoolReturnRejection.None));
        }

        [Test]
        public void SceneScopes_WithSameSceneHandle_HaveUniqueOwnerIdentity()
        {
            var first = UIPoolScope.CreateScene(42, "Scene");
            var second = UIPoolScope.CreateScene(42, "Scene");

            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(first.Kind, Is.EqualTo(UIPoolScopeKind.Scene));
            Assert.That(second.Kind, Is.EqualTo(UIPoolScopeKind.Scene));
        }

        [Test]
        public void Capacity_UsesPriorityThenDeterministicLru()
        {
            var clock = new ManualClock();
            var pool = new UIObjectPool(2, clock);
            var low = CreateEntry("low");
            var high = CreateEntry("high");
            var medium = CreateEntry("medium");

            Return(pool, low, new UIPoolPolicy(true, 2, priority: 0));
            clock.Advance(1);
            Return(pool, high, new UIPoolPolicy(true, 2, priority: 10));
            clock.Advance(1);

            Assert.That(
                pool.TryRelease(
                    typeof(TestContext),
                    medium,
                    new UIPoolPolicy(true, 2, priority: 5),
                    UIPoolScope.Global,
                    "Open",
                    false,
                    out var overflow,
                    out var rejection),
                Is.True);
            Assert.That(rejection, Is.EqualTo(UIPoolReturnRejection.None));
            Assert.That(overflow, Is.SameAs(low));

            var snapshot = pool.GetDiagnostics();
            Assert.That(snapshot.IdleCount, Is.EqualTo(2));
            Assert.That(snapshot.EvictionCount, Is.EqualTo(1));
            Assert.That(snapshot.Entries[0].Priority, Is.GreaterThanOrEqualTo(5));
            Assert.That(snapshot.Entries[1].Priority, Is.GreaterThanOrEqualTo(5));
        }

        [Test]
        public void ExpiryAndScopeRelease_RemoveOnlyMatchingIdleEntries()
        {
            var clock = new ManualClock();
            var pool = new UIObjectPool(4, clock);
            var module = UIPoolScope.CreateModule("Inventory");
            var expiring = CreateEntry("expiring");
            var scoped = CreateEntry("scoped");
            var global = CreateEntry("global");

            Return(
                pool,
                expiring,
                new UIPoolPolicy(true, 4, idleTimeoutSeconds: 2));
            Return(
                pool,
                scoped,
                new UIPoolPolicy(true, 4),
                module);
            Return(pool, global, new UIPoolPolicy(true, 4));
            clock.Advance(3);

            Assert.That(pool.EvictExpired(), Is.EqualTo(1));
            Assert.That(pool.ClearScope(module), Is.EqualTo(1));
            Assert.That(pool.GetDiagnostics().IdleCount, Is.EqualTo(1));
            Assert.That(pool.TryGet(typeof(TestContext), out var remaining), Is.True);
            Assert.That(remaining, Is.SameAs(global));
        }

        [Test]
        public void Clear_ContinuesAfterIndividualCleanupFailures()
        {
            var pool = new UIObjectPool();
            Return(pool, CreateEntry("first"), new UIPoolPolicy(true, 2));
            Return(pool, CreateEntry("second"), new UIPoolPolicy(true, 2));
            var attempts = 0;

            var error = Assert.Throws<AggregateException>(() =>
                pool.Clear(_ =>
                {
                    attempts++;
                    throw new InvalidOperationException("cleanup");
                }));

            Assert.That(error.Flatten().InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(attempts, Is.EqualTo(2));
            Assert.That(pool.GetDiagnostics().IdleCount, Is.Zero);
        }

        private UIPooledObject CreateEntry(string name)
        {
            var view = new GameObject(name, typeof(RectTransform), typeof(UIView));
            _objects.Add(view);
            return new UIPooledObject(
                typeof(TestContext),
                $"Tests/{name}",
                new TestContext(),
                view);
        }

        private static void Return(
            UIObjectPool pool,
            UIPooledObject entry,
            UIPoolPolicy policy,
            UIPoolScope scope = default)
        {
            scope = scope.Kind == UIPoolScopeKind.Global
                ? UIPoolScope.Global
                : scope;
            Assert.That(
                pool.TryRelease(
                    typeof(TestContext),
                    entry,
                    policy,
                    scope,
                    "Open",
                    false,
                    out _,
                    out var rejection),
                Is.True,
                rejection.ToString());
        }

        private sealed class ManualClock : IUIPoolClock
        {
            public long Timestamp { get; private set; }
            public long Frequency => 1;
            public void Advance(long seconds) => Timestamp += seconds;
        }

        private sealed class TestContext : BaseContext
        {
            public override UILayer DefaultLayer => UILayer.Normal;
        }
    }
}
