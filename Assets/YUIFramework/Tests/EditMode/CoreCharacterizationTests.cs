using System;
using NUnit.Framework;
using UnityEngine;

namespace YUIFramework.Tests
{
    public sealed class CoreCharacterizationTests
    {
        [Test]
        public void Pool_ReleaseAndGet_ReturnsSameObject()
        {
            var pool = new UIObjectPool();
            var entry = CreatePooledObject();

            var released = pool.TryRelease(
                typeof(TestContext),
                entry,
                new UIPoolPolicy(true, 1),
                out var overflow);

            Assert.That(released, Is.True);
            Assert.That(overflow, Is.Null);
            Assert.That(pool.Count(typeof(TestContext)), Is.EqualTo(1));
            Assert.That(pool.TryGet(typeof(TestContext), out var restored), Is.True);
            Assert.That(restored, Is.SameAs(entry));
            Assert.That(pool.Count(typeof(TestContext)), Is.Zero);

            Destroy(entry.ViewObject);
        }

        [Test]
        public void Pool_ReleasePastCapacity_ReturnsOverflow()
        {
            var pool = new UIObjectPool();
            var first = CreatePooledObject();
            var second = CreatePooledObject();
            var policy = new UIPoolPolicy(true, 1);

            Assert.That(pool.TryRelease(typeof(TestContext), first, policy, out _), Is.True);
            Assert.That(pool.TryRelease(typeof(TestContext), second, policy, out var overflow), Is.False);
            Assert.That(overflow, Is.SameAs(second));
            Assert.That(pool.Count(typeof(TestContext)), Is.EqualTo(1));

            pool.Clear(_ => { });
            Destroy(first.ViewObject);
            Destroy(second.ViewObject);
        }

        [Test]
        public void Pool_DisabledPolicy_DoesNotCache()
        {
            var pool = new UIObjectPool();
            var entry = CreatePooledObject();

            var released = pool.TryRelease(
                typeof(TestContext),
                entry,
                new UIPoolPolicy(false, 1),
                out var overflow);

            Assert.That(released, Is.False);
            Assert.That(overflow, Is.SameAs(entry));
            Assert.That(pool.Count(typeof(TestContext)), Is.Zero);

            Destroy(entry.ViewObject);
        }

        [Test]
        public void Pool_Clear_InvokesDestroyActionForEveryEntry()
        {
            var pool = new UIObjectPool();
            var first = CreatePooledObject();
            var second = CreatePooledObject();
            var policy = new UIPoolPolicy(true, 2);
            var destroyed = 0;

            pool.TryRelease(typeof(TestContext), first, policy, out _);
            pool.TryRelease(typeof(TestContext), second, policy, out _);
            pool.Clear(entry =>
            {
                destroyed++;
                Destroy(entry.ViewObject);
            });

            Assert.That(destroyed, Is.EqualTo(2));
            Assert.That(pool.Count(typeof(TestContext)), Is.Zero);
        }

        [Test]
        public void MessageCenter_TypedSubscription_ReceivesPayload()
        {
            var messages = new UIMessageCenter();
            var received = 0;
            using var token = messages.Subscribe<int>("score.changed", value => received = value);

            messages.Publish("score.changed", 42);

            Assert.That(received, Is.EqualTo(42));
            Assert.That(messages.ListenerCount, Is.EqualTo(1));
        }

        [Test]
        public void MessageCenter_DisposedSubscription_StopsReceiving()
        {
            var messages = new UIMessageCenter();
            var received = 0;
            var token = messages.Subscribe("refresh", () => received++);

            messages.Publish("refresh");
            token.Dispose();
            messages.Publish("refresh");

            Assert.That(received, Is.EqualTo(1));
            Assert.That(messages.ListenerCount, Is.Zero);
        }

        [Test]
        public void MessageCenter_UnsubscribeOwner_RemovesOnlyOwnedSubscriptions()
        {
            var messages = new UIMessageCenter();
            var firstOwner = new object();
            var secondOwner = new object();
            messages.Subscribe("refresh", () => { }, firstOwner);
            messages.Subscribe("refresh", () => { }, secondOwner);

            messages.UnsubscribeOwner(firstOwner);

            Assert.That(messages.Count("refresh"), Is.EqualTo(1));
        }

        [Test]
        public void ObservableProperty_ChangedValue_NotifiesOldAndNewValues()
        {
            var property = new ObservableProperty<int>(10);
            var oldValue = 0;
            var newValue = 0;
            using var subscription = property.Subscribe(
                (oldItem, newItem) =>
                {
                    oldValue = oldItem;
                    newValue = newItem;
                },
                false);

            property.Value = 20;

            Assert.That(oldValue, Is.EqualTo(10));
            Assert.That(newValue, Is.EqualTo(20));
        }

        [Test]
        public void ObservableProperty_EqualValue_DoesNotNotify()
        {
            var property = new ObservableProperty<string>("same");
            var notifications = 0;
            using var subscription = property.Subscribe(_ => notifications++, false);

            property.Value = "same";

            Assert.That(notifications, Is.Zero);
        }

        [Test]
        public void ObservableProperty_DisposedSubscription_StopsReceiving()
        {
            var property = new ObservableProperty<int>();
            var notifications = 0;
            var subscription = property.Subscribe(_ => notifications++, false);

            subscription.Dispose();
            property.Value = 1;

            Assert.That(notifications, Is.Zero);
        }

        [Test]
        public void UIConfig_ToTransitionOptions_NormalizesInvalidValues()
        {
            var config = new UIConfig
            {
                ShowDuration = -1f,
                HideDuration = -2f,
                SlideDistance = -3f,
                StartScale = 0f
            };

            var options = config.ToTransitionOptions();

            Assert.That(options.ShowDuration, Is.Zero);
            Assert.That(options.HideDuration, Is.Zero);
            Assert.That(options.SlideDistance, Is.Zero);
            Assert.That(options.StartScale, Is.EqualTo(0.01f));
        }

        [Test]
        public void PoolPolicy_FromConfig_CopiesCacheSettings()
        {
            var config = new UIConfig
            {
                CacheOnClose = true,
                MaxPoolSize = 3
            };

            var policy = UIPoolPolicy.FromConfig(config);

            Assert.That(policy.CacheOnClose, Is.True);
            Assert.That(policy.MaxPoolSize, Is.EqualTo(3));
        }

        [Test]
        public void PoolPolicy_NegativeCapacity_NormalizesToZero()
        {
            var policy = new UIPoolPolicy(true, -10);

            Assert.That(policy.MaxPoolSize, Is.Zero);
        }

        [Test]
        public void UIKey_EqualOrdinalValues_AreEqual()
        {
            var first = new UIKey("MainMenu");
            var second = new UIKey("MainMenu");

            Assert.That(first, Is.EqualTo(second));
            Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
        }

        [Test]
        public void UIKey_DifferentCaseValues_AreNotEqual()
        {
            Assert.That(new UIKey("MainMenu"), Is.Not.EqualTo(new UIKey("mainmenu")));
        }

        [Test]
        public void UIKey_EmptyValue_IsRejected()
        {
            Assert.Throws<ArgumentException>(() => new UIKey(" "));
        }

        [TestCase(UIContextState.Unloaded, UIContextState.Loading)]
        [TestCase(UIContextState.Loading, UIContextState.Initializing)]
        [TestCase(UIContextState.Initializing, UIContextState.Opening)]
        [TestCase(UIContextState.Opening, UIContextState.Opened)]
        [TestCase(UIContextState.Opened, UIContextState.Hiding)]
        [TestCase(UIContextState.Hiding, UIContextState.Hidden)]
        [TestCase(UIContextState.Hidden, UIContextState.Closing)]
        [TestCase(UIContextState.Closing, UIContextState.Pooled)]
        [TestCase(UIContextState.Pooled, UIContextState.Opening)]
        [TestCase(UIContextState.Closing, UIContextState.Releasing)]
        [TestCase(UIContextState.Faulted, UIContextState.Releasing)]
        [TestCase(UIContextState.Releasing, UIContextState.Released)]
        public void LifecycleGraph_ProductionTransitions_AreAllowed(
            UIContextState from,
            UIContextState to)
        {
            Assert.That(UIContextStateGraph.CanTransition(from, to), Is.True);
        }

        [TestCase(UIContextState.Unloaded, UIContextState.Opened)]
        [TestCase(UIContextState.Loading, UIContextState.Pooled)]
        [TestCase(UIContextState.Opened, UIContextState.Pooled)]
        [TestCase(UIContextState.Hidden, UIContextState.Released)]
        [TestCase(UIContextState.Pooled, UIContextState.Opened)]
        [TestCase(UIContextState.Released, UIContextState.Opening)]
        public void LifecycleGraph_IllegalShortcuts_AreRejected(
            UIContextState from,
            UIContextState to)
        {
            Assert.That(UIContextStateGraph.CanTransition(from, to), Is.False);
        }

        [Test]
        public void LifecycleState_LegacyNames_MapToStableY2States()
        {
            Assert.That(UIContextState.None, Is.EqualTo(UIContextState.Unloaded));
            Assert.That(UIContextState.Shown, Is.EqualTo(UIContextState.Opened));
            Assert.That(UIContextState.Closed, Is.EqualTo(UIContextState.Pooled));
            Assert.That(UIContextState.Destroyed, Is.EqualTo(UIContextState.Released));
        }

        private static UIPooledObject CreatePooledObject()
        {
            var viewObject = new GameObject("PooledTestView", typeof(RectTransform), typeof(UIView));
            var context = new TestContext();
            return new UIPooledObject(typeof(TestContext), "Tests/Pooled", context, viewObject);
        }

        private static void Destroy(UnityEngine.Object target)
        {
            if (target != null)
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private sealed class TestContext : BaseContext
        {
            public override UILayer DefaultLayer => UILayer.Normal;
        }
    }
}
