using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace YUIFramework.Tests
{
    public sealed class Stage10ListContractsTests
    {
        [Test]
        public void CollectionMovesSelectionByIdentityAndRejectsDuplicateBeforeCommit()
        {
            var items = new ObservableCollection<string>();
            items.Reset(new[] { "a", "b", "c" });
            using var source = new UIListDataSource<string>(items, item => item);
            using var selection = new UIListSelection(source, true);
            selection.Set("b", true);
            items.Insert(0, "x");
            items.Move(2, 3);
            Assert.That(source.IndexOf("b"), Is.EqualTo(3));
            Assert.That(selection.Contains("b"), Is.True);
            Assert.Throws<ArgumentException>(() => items.Insert(0, "b"));
            Assert.That(items.Count, Is.EqualTo(4));
            Assert.Throws<ArgumentException>(() => items.Replace(0, "b"));
            Assert.Throws<ArgumentException>(() => items.Reset(new[] { "b", "b" }));
            Assert.That(source.GetId(3), Is.EqualTo("b"));
            items.Remove("b");
            Assert.That(selection.Count, Is.Zero);
        }

        [Test]
        public void CollectionObserverFailureDoesNotBlockOtherViewsAndReentrancyIsExplicit()
        {
            var items = new ObservableCollection<int>();
            var notifications = 0;
            items.CollectionChanged += _ => throw new InvalidOperationException("observer");
            items.CollectionChanged += _ => notifications++;
            items.CollectionChanged += _ => items.Add(2);
            var error = Assert.Throws<AggregateException>(() => items.Add(1));
            Assert.That(items.Count, Is.EqualTo(1));
            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(error.InnerExceptions.Count, Is.EqualTo(2));
        }

        [Test]
        public void BindingGenerationRejectsLateWritesAndCleansAllResources()
        {
            var root = new GameObject("Item", typeof(RectTransform), typeof(UIListItemLifetime));
            try
            {
                var lifetime = root.GetComponent<UIListItemLifetime>();
                var old = lifetime.BeginBind("a", 0, false);
                var token = old.Token;
                var current = lifetime.BeginBind("b", 1, true);
                Assert.That(token.IsCancellationRequested, Is.True);
                Assert.That(old.TryApply(_ => Assert.Fail("stale write")), Is.False);
                Assert.That(current.IsCurrent, Is.True);
                lifetime.EndBind();
                Assert.That(current.IsCurrent, Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

#pragma warning disable CS0618
        [Test]
        public void MissingVendorFacadeFailsWithInstallInstructionInsteadOfRunningAnotherKernel()
        {
            var factory = UIVirtualListBackend.Factory;
            var root = new GameObject("Legacy", typeof(RectTransform), typeof(ScrollRect), typeof(UIVirtualList));
            try
            {
                UIVirtualListBackend.Factory = null;
                var error = Assert.Throws<InvalidOperationException>(() => root.GetComponent<UIVirtualList>().ReloadData());
                Assert.That(error.Message, Does.Contain("Install.ps1"));
            }
            finally { UIVirtualListBackend.Factory = factory; UnityEngine.Object.DestroyImmediate(root); }
        }
#pragma warning restore CS0618
    }
}
