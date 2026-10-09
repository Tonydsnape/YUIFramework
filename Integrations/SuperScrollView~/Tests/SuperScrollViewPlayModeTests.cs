using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace YUIFramework.Integrations.Tests
{
    public sealed class SuperScrollViewPlayModeTests
    {
        private GameObject _root;
        private GameObject _prefab;
        private readonly List<IDisposable> _owners = new List<IDisposable>();
        private ObservableCollection<string> _rows;
        private UIListDataSource<string> _source;

        [SetUp]
        public void Setup()
        {
            _root = new GameObject("ListTestCanvas", typeof(RectTransform), typeof(Canvas));
            ((RectTransform)_root.transform).sizeDelta = new Vector2(500, 400);
            _root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            _prefab = new GameObject("ListItem", typeof(RectTransform), typeof(Image));
            _prefab.SetActive(false);
            _rows = new ObservableCollection<string>();
            var data = new string[10000];
            for (var index = 0; index < data.Length; index++) data[index] = "id-" + index;
            _rows.Reset(data);
            _source = new UIListDataSource<string>(_rows, row => row);
            _owners.Add(_source);
        }
        [TearDown]
        public void Teardown()
        {
            for (var index = _owners.Count - 1; index >= 0; index--) _owners[index].Dispose();
            _owners.Clear();
            if (_prefab != null) Object.DestroyImmediate(_prefab);
            if (_root != null) Object.DestroyImmediate(_root);
        }
        private SuperScrollViewList<string> Create(UIListBackendLayout layout, bool dynamic = false,
            Action<UIListItemBinding, string> bind = null, UIListSelection selection = null)
        {
            var list = new SuperScrollViewList<string>((RectTransform)_root.transform, _source,
                new SuperScrollViewOptions { Layout = layout, ItemSize = new Vector2(100, 40), DynamicSize = dynamic, Columns = 4 },
                _prefab, bind ?? ((item, row) => { Assert.That(item.Id, Is.EqualTo(row)); }), selection);
            _owners.Add(list);
            _owners.Add(list.BeginDisplay());
            return list;
        }

        [UnityTest]
        public IEnumerator TenThousandRowsUseBoundedNativeListAndGridPools()
        {
            foreach (var layout in new[] { UIListBackendLayout.Vertical, UIListBackendLayout.Horizontal, UIListBackendLayout.Grid })
            {
                var list = Create(layout);
                yield return null;
                for (var index = 0; index < 10000; index += 137)
                {
                    list.ScrollTo(index);
                    yield return null;
                    Assert.That(list.GetVisibleItem("id-" + index), Is.Not.Null, $"{layout}:{index}");
                    Assert.That(list.BoundItemCount, Is.InRange(1, 100));
                    Assert.That(list.CreatedItemCount, Is.LessThan(150));
                }
                TestContext.WriteLine($"{layout}: data=10000, created={list.CreatedItemCount}, bound={list.BoundItemCount}");
                list.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator ChangesRetainStableSelectionAndAnchorOffsetAndRefreshOnlyChangedItem()
        {
            using var selection = new UIListSelection(_source, true);
            var calls = 0;
            var list = Create(UIListBackendLayout.Vertical, bind: (_, __) => calls++, selection: selection);
            list.ScrollTo(500, 12);
            yield return null;
            var anchor = list.CaptureAnchor();
            selection.Set(anchor.Id, true);
            _rows.Insert(0, "new");
            var after = list.CaptureAnchor();
            Assert.That(after.Id, Is.EqualTo(anchor.Id));
            Assert.That(after.Position.y, Is.EqualTo(anchor.Position.y).Within(0.2f));
            _rows.Move(0, 1000);
            Assert.That(list.CaptureAnchor().Id, Is.EqualTo(anchor.Id));
            Assert.That(selection.Contains(anchor.Id), Is.True);
            var before = calls;
            _rows.Replace(_source.IndexOf(anchor.Id), anchor.Id);
            Assert.That(calls - before, Is.EqualTo(1));
            _rows.Remove(anchor.Id);
            Assert.That(selection.Contains(anchor.Id), Is.False);
            _rows.Clear();
            Assert.That(list.BoundItemCount, Is.Zero);
            _rows.Add("recovered");
            Assert.That(list.GetVisibleItem("recovered"), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator DynamicSizesApplyInBothDirectionsAndRetainAnchor()
        {
            foreach (var direction in new[] { UIListBackendLayout.Vertical, UIListBackendLayout.Horizontal })
            {
                var horizontal = direction == UIListBackendLayout.Horizontal;
                var list = Create(direction, true, (binding, _) =>
                {
                    var rect = (RectTransform)binding.View.transform;
                    rect.SetSizeWithCurrentAnchors(horizontal ? RectTransform.Axis.Horizontal : RectTransform.Axis.Vertical,
                        binding.Index % 2 == 0 ? 45 : 75);
                });
                list.ScrollTo(200);
                yield return null;
                var anchor = list.CaptureAnchor();
                var item = list.GetVisibleItem(anchor.Id);
                var rect = (RectTransform)item.transform;
                rect.SetSizeWithCurrentAnchors(horizontal ? RectTransform.Axis.Horizontal : RectTransform.Axis.Vertical, 93);
                list.NotifySizeChanged(item.Index);
                var position = list.CaptureAnchor();
                Assert.That(position.Id, Is.EqualTo(anchor.Id));
                Assert.That(horizontal ? position.Position.x : position.Position.y,
                    Is.EqualTo(horizontal ? anchor.Position.x : anchor.Position.y).Within(0.2f));
                Assert.That(horizontal ? rect.rect.width : rect.rect.height, Is.EqualTo(93));
                list.ScrollTo(9999);
                yield return null;
                Assert.That(list.GetVisibleItem("id-9999"), Is.Not.Null);
                list.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator DisplayReuseOneThousandTimesDoesNotAccumulateItemsOrObservers()
        {
            var binds = 0;
            var list = new SuperScrollViewList<string>((RectTransform)_root.transform, _source,
                new SuperScrollViewOptions(), _prefab, (_, __) => binds++);
            _owners.Add(list);
            var max = 0;
            for (var index = 0; index < 1000; index++)
            {
                var display = list.BeginDisplay();
                list.ScrollTo(index * 7);
                max = Mathf.Max(max, list.CreatedItemCount);
                display.Dispose();
                var oldBinds = binds;
                _rows.Replace(0, "id-0");
                Assert.That(binds, Is.EqualTo(oldBinds));
                Assert.That(list.BoundItemCount, Is.Zero);
            }
            Assert.That(max, Is.LessThan(100));
            TestContext.WriteLine($"1000 display cycles: created={max}, boundAfterHide={list.BoundItemCount}");
            yield return null;
        }

        [UnityTest]
        public IEnumerator SteadyRealScrollMeasuresVendorAndAdapterAllocation()
        {
            var list = Create(UIListBackendLayout.Vertical, bind: (_, __) => { });
            for (var frame = 0; frame < 100; frame++)
            {
                list.ScrollTo(frame * 10);
                yield return null;
            }
            long total = 0;
            var nativeBefore = list.CreatedItemCount;
            for (var batch = 0; batch < 100; batch++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                list.ScrollTo(1000 + batch * 10);
                total += GC.GetAllocatedBytesForCurrentThread() - before;
                yield return null;
            }
            TestContext.WriteLine($"Real vendor ScrollTo: {total} managed bytes / 100 calls; nativeBefore={nativeBefore}, nativeAfter={list.CreatedItemCount}; spriteLeases=0 (no image binder).");
            Assert.That(total, Is.LessThanOrEqualTo(4096));
            Assert.That(list.CreatedItemCount, Is.LessThanOrEqualTo(nativeBefore + 2));
            var native = list.Scroll.GetComponent<SuperScrollView.LoopListView2>();
            total = 0;
            for (var frame = 0; frame < 200; frame++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                list.Scroll.content.anchoredPosition += new Vector2(0, 20);
                native.UpdateListView(150, 150, 100, 100);
                total += GC.GetAllocatedBytesForCurrentThread() - before;
                yield return null;
            }
            TestContext.WriteLine($"Continuous content scroll + native UpdateListView: {total} managed bytes / 200 steps; created={list.CreatedItemCount}. Excludes player-loop/test-runner and deferred uGUI rebuild allocations.");
            Assert.That(total, Is.LessThanOrEqualTo(4096));
        }

        [UnityTest]
        public IEnumerator FailedBinderIsObservableAndLaterRefreshStillWorks()
        {
            var fail = false;
            var list = Create(UIListBackendLayout.Vertical, bind: (_, __) =>
            { if (fail) throw new InvalidOperationException("bind-failure"); });
            Exception observed = null;
            list.Error += error => observed = error;
            fail = true;
            list.RefreshItem(0);
            Assert.That(observed, Is.TypeOf<InvalidOperationException>());
            fail = false;
            list.RefreshItem(0);
            Assert.That(list.GetVisibleItem("id-0"), Is.Not.Null);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ThrowingItemCannotPreventSuspensionCleanupOrAnchorResume()
        {
            var list = Create(UIListBackendLayout.Vertical);
            list.ScrollTo(200);
            yield return null;
            var anchor = list.CaptureAnchor();
            var first = list.GetVisibleItem(anchor.Id);
            var second = list.GetVisibleItem("id-" + (first.Index + 1));
            var firstBinding = first.BeginBind(first.Id, first.Index, false);
            var secondBinding = second.BeginBind(second.Id, second.Index, false);
            var secondToken = secondBinding.Token;
            using var bad = firstBinding.Token.Register(() => throw new InvalidOperationException("cancel-cleanup"));
            Assert.Throws<AggregateException>(() => list.SetSuspended(true));
            Assert.That(list.BoundItemCount, Is.Zero);
            Assert.That(secondToken.IsCancellationRequested, Is.True);
            Assert.That(list.Scroll.gameObject.activeSelf, Is.False);
            list.SetSuspended(false);
            Assert.That(list.IsDisplaying, Is.True);
            Assert.That(list.CaptureAnchor().Id, Is.EqualTo(anchor.Id));
            Assert.That(list.CaptureAnchor().Position.y, Is.EqualTo(anchor.Position.y).Within(0.2f));
        }

        [UnityTest]
        public IEnumerator NativeTouchDragRebaseAndNestedForwardingRemainPaired()
        {
            var events = new GameObject("DragEventSystem", typeof(EventSystem));
            try
            {
                var parent = Create(UIListBackendLayout.Horizontal);
                var child = Create(UIListBackendLayout.Vertical, true);
                var parentScroll = (UINestedScrollRect)parent.Scroll;
                var childScroll = (UINestedScrollRect)child.Scroll;
                childScroll.SetParentScroll(parentScroll);
                var parentNative = parent.Scroll.GetComponent<SuperScrollView.LoopListView2>();
                var childNative = child.Scroll.GetComponent<SuperScrollView.LoopListView2>();
                var data = new PointerEventData(events.GetComponent<EventSystem>())
                { pointerId = 7, button = PointerEventData.InputButton.Left, pressPosition = new Vector2(100, 100), position = new Vector2(200, 102) };
                ExecuteEvents.Execute(child.Scroll.gameObject, data, ExecuteEvents.beginDragHandler);
                Assert.That(parentNative.IsDraging, Is.True);
                Assert.That(childNative.IsDraging, Is.False);
                ExecuteEvents.Execute(child.Scroll.gameObject, data, ExecuteEvents.dragHandler);
                child.SetSuspended(true);
                Assert.That(parentNative.IsDraging, Is.False);
                child.SetSuspended(false);
                data.position = new Vector2(102, 200);
                var starts = 0;
                childNative.mOnBeginDragAction = () => starts++;
                ExecuteEvents.Execute(child.Scroll.gameObject, data, ExecuteEvents.beginDragHandler);
                Assert.That(childNative.IsDraging, Is.True);
                child.RefreshItem(0);
                child.NotifySizeChanged(0);
                Assert.That(childScroll.ActivePointerId, Is.EqualTo(7));
                Assert.That(starts, Is.EqualTo(1));
                ExecuteEvents.Execute(child.Scroll.gameObject, data, ExecuteEvents.dragHandler);
                ExecuteEvents.Execute(child.Scroll.gameObject, data, ExecuteEvents.endDragHandler);
                Assert.That(childNative.IsDraging, Is.False);
                Assert.That(childScroll.ActivePointerId, Is.Null);
                ExecuteEvents.Execute(child.Scroll.gameObject, data, ExecuteEvents.beginDragHandler);
                var input = _root.AddComponent<CanvasGroup>();
                input.interactable = false;
                Assert.That(childNative.IsDraging, Is.False);
                ExecuteEvents.Execute(child.Scroll.gameObject, data, ExecuteEvents.beginDragHandler);
                Assert.That(childNative.IsDraging, Is.False, "Input composition cannot be bypassed by an existing pointer.");
                input.interactable = true;
                var grid = Create(UIListBackendLayout.Grid);
                ExecuteEvents.Execute(grid.Scroll.gameObject, data, ExecuteEvents.beginDragHandler);
                var gridNative = grid.Scroll.GetComponent<SuperScrollView.LoopGridView>();
                Assert.That(gridNative.IsDraging, Is.True);
                grid.SetSuspended(true);
                Assert.That(gridNative.IsDraging, Is.False);
                yield return null;
            }
            finally { Object.DestroyImmediate(events); }
        }

        [UnityTest]
        public IEnumerator GridChangesPreserveAnchorAndCellPositions()
        {
            var list = Create(UIListBackendLayout.Grid);
            list.ScrollTo(800);
            yield return null;
            var anchor = list.CaptureAnchor();
            var first = list.GetVisibleItem(anchor.Id);
            var firstPosition = first.transform.localPosition;
            var adjacent = list.GetVisibleItem(_source.GetId(first.Index + 1));
            Assert.That(adjacent, Is.Not.Null);
            Assert.That(adjacent.transform.localPosition.x - firstPosition.x, Is.EqualTo(104).Within(0.1f));
            _rows.Insert(0, "new-grid");
            var current = list.GetVisibleItem(anchor.Id);
            Assert.That(current, Is.Not.Null);
            var offset = list.Scroll.viewport.InverseTransformPoint(current.transform.position);
            Assert.That(offset.y, Is.EqualTo(anchor.Position.y).Within(0.2f));
            _rows.Move(0, 2000);
            _rows.RemoveAt(0);
            list.RefreshItem(_source.IndexOf(anchor.Id));
            Assert.That(list.GetVisibleItem(anchor.Id).Index, Is.EqualTo(_source.IndexOf(anchor.Id)));
        }

        [UnityTest]
        public IEnumerator CallerCancellationDuringPrefabLoadNeverCreatesListOrStrandsNativeHandle()
        {
            var provider = new GateProvider(_prefab) { DelayPrefab = true };
            var resources = new UIResourceService();
            resources.Packages.Register(provider, true);
            using var cancellation = new CancellationTokenSource();
            var create = SuperScrollViewList<string>.CreateAsync((RectTransform)_root.transform, _source,
                new SuperScrollViewOptions(), resources, UIResourceKey.Of<GameObject>("prefab"),
                (_, __) => { }, cancellationToken: cancellation.Token).AsTask();
            cancellation.Cancel();
            provider.Complete("prefab");
            for (var frame = 0; frame < 120 && !create.IsCompleted; frame++) yield return null;
            Assert.That(create.IsCompleted, Is.True);
            Assert.That(create.IsCanceled || create.Exception?.GetBaseException() is OperationCanceledException, Is.True);
            Assert.That(_root.transform.childCount, Is.Zero);
            Assert.That(resources.GetDiagnostics().TotalLeases, Is.Zero);
            yield return Await(resources.ShutdownAsync().AsTask());
            Assert.That(provider.Released, Is.EqualTo(1));
            provider.DestroySprites();
        }

        [UnityTest]
        public IEnumerator ContextPoolNavigationCancellationAndShutdownOwnDisplayBinding()
        {
            var uiRoot = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(UIRoot));
            var loader = new PageLoader();
            var manager = new UIManager();
            manager.Initialize(loader, new UIObjectPool());
            ListPage.Source = _source;
            ListPage.Prefab = _prefab;
            ListPage.Binds = 0;
            ListPage.Last = null;
            var config = new UIConfig { Id = "ListPage", PrefabKey = "page", CacheOnClose = true, MaxPoolSize = 1, UseTransition = false, FullScreen = true };
            manager.Register<ListPage>(config);
            manager.Register<CoverPage>(new UIConfig { Id = "Cover", PrefabKey = "cover", FullScreen = true, UseTransition = false });
            try
            {
                for (var cycle = 0; cycle < 1000; cycle++)
                {
                    var open = manager.OpenAsync<ListPage>().AsTask();
                    yield return Await(open);
                    Assert.That(open.Result.List.IsDisplaying, Is.True);
                    yield return Await(manager.CloseAsync(open.Result).AsTask());
                    var before = ListPage.Binds;
                    _rows.Replace(0, "id-0");
                    Assert.That(ListPage.Binds, Is.EqualTo(before));
                    Assert.That(open.Result.List.BoundItemCount, Is.Zero);
                }
                var push = manager.Navigator.PushAsync<ListPage>().AsTask();
                yield return Await(push);
                yield return Await(manager.Navigator.PushAsync<CoverPage>().AsTask());
                Assert.That(push.Result.IsSuspended, Is.True);
                Assert.That(push.Result.List.BoundItemCount, Is.Zero);
                yield return Await(manager.Navigator.PopAsync().AsTask());
                Assert.That(push.Result.List.IsDisplaying, Is.True);

                var gate = new CancelHide();
                manager.Transitions.RegisterCustom("list-cancel", gate);
                config.UseTransition = true;
                config.TransitionType = UITransitionType.Custom;
                config.CustomTransitionId = "list-cancel";
                using var cancellation = new CancellationTokenSource();
                var close = manager.CloseAsync(push.Result, cancellation.Token).AsTask();
                for (var frame = 0; frame < 30 && !gate.Started; frame++) yield return null;
                Assert.That(gate.Started, Is.True);
                cancellation.Cancel();
                for (var frame = 0; frame < 30 && !close.IsCompleted; frame++) yield return null;
                Assert.That(close.IsCompleted, Is.True);
                Assert.That(close.IsCanceled || close.Exception?.GetBaseException() is OperationCanceledException, Is.True);
                Assert.That(push.Result.State, Is.EqualTo(UIContextState.Opened));
                Assert.That(push.Result.List.IsDisplaying, Is.True);
                config.UseTransition = false;
                var list = push.Result.List;
                yield return Await(manager.ShutdownAsync().AsTask());
                Assert.That(list.IsDisposed, Is.True);
                var final = ListPage.Binds;
                _rows.Replace(0, "id-0");
                Assert.That(ListPage.Binds, Is.EqualTo(final));
            }
            finally
            {
                if (manager.IsInitialized) manager.ShutdownAsync().Forget(Debug.LogException);
                loader.Dispose();
                Object.DestroyImmediate(uiRoot);
                ListPage.Source = null; ListPage.Prefab = null; ListPage.Last = null;
            }
        }

        [UnityTest]
        public IEnumerator LateSpriteAfterOffscreenHideAndExternalDestroyReleasesAllLeases()
        {
            var provider = new GateProvider(_prefab);
            var resources = new UIResourceService();
            resources.Packages.Register(provider, true);
            var create = SuperScrollViewList<string>.CreateAsync((RectTransform)_root.transform, _source,
                new SuperScrollViewOptions(), resources, UIResourceKey.Of<GameObject>("prefab"), (_, __) => { }).AsTask();
            yield return Await(create);
            var list = create.Result;
            _owners.Add(list);
            var display = list.BeginDisplay();
            var item = list.GetVisibleItem("id-0");
            var image = item.GetComponent<Image>();
            var binding = item.BeginBind("id-0", 0, false);
            var request = binding.LoadSpriteAsync(new NonCooperativeService(resources), UIResourceKey.Of<Sprite>("A"), image).AsTask();
            list.ScrollTo(5000);
            provider.Complete("A");
            yield return Await(request);
            Assert.That(request.Result, Is.False);
            Assert.That(resources.GetDiagnostics().TotalLeases, Is.EqualTo(1));
            var current = list.GetVisibleItem("id-5000");
            var currentImage = current.GetComponent<Image>();
            var second = current.BeginBind("id-5000", 5000, false)
                .LoadSpriteAsync(new NonCooperativeService(resources), UIResourceKey.Of<Sprite>("B"), currentImage).AsTask();
            display.Dispose();
            provider.Complete("B");
            yield return Await(second);
            Assert.That(second.Result, Is.False);
            var reopen = list.BeginDisplay();
            Object.DestroyImmediate(list.Scroll.gameObject);
            yield return null;
            yield return null;
            Assert.That(list.IsDisposed, Is.True);
            Assert.That(resources.GetDiagnostics().TotalLeases, Is.Zero);
            reopen.Dispose();
            yield return Await(resources.ShutdownAsync().AsTask());
            Assert.That(provider.Released, Is.EqualTo(3));
            provider.DestroySprites();
        }

        [UnityTest]
        public IEnumerator NativePrefabLeaseOutlivesItemsAndLateSpriteCannotOverwriteReuse()
        {
            var provider = new GateProvider(_prefab);
            var resources = new UIResourceService();
            resources.Packages.Register(provider, true);
            var create = SuperScrollViewList<string>.CreateAsync((RectTransform)_root.transform, _source,
                new SuperScrollViewOptions(), resources, UIResourceKey.Of<GameObject>("prefab"),
                (_, __) => { }).AsTask();
            yield return Await(create);
            var list = create.Result;
            _owners.Add(list);
            var display = list.BeginDisplay();
            Assert.That(resources.GetDiagnostics().TotalLeases, Is.EqualTo(1));
            var item = list.GetVisibleItem("id-0");
            var image = item.GetComponent<Image>();
            var nonCooperative = new NonCooperativeService(resources);
            var bindingA = item.BeginBind("A", 0, false);
            var late = bindingA.LoadSpriteAsync(nonCooperative, UIResourceKey.Of<Sprite>("A"), image).AsTask();
            var bindingB = item.BeginBind("B", 1, false);
            var fast = bindingB.LoadSpriteAsync(nonCooperative, UIResourceKey.Of<Sprite>("B"), image).AsTask();
            provider.Complete("B");
            yield return Await(fast);
            Assert.That(fast.Result, Is.True);
            Assert.That(image.sprite, Is.SameAs(provider.SpriteB));
            provider.Complete("A");
            yield return Await(late);
            Assert.That(late.Result, Is.False);
            Assert.That(image.sprite, Is.SameAs(provider.SpriteB));
            Assert.That(resources.GetDiagnostics().TotalLeases, Is.EqualTo(2));
            display.Dispose();
            Assert.That(image.sprite, Is.Null);
            Assert.That(resources.GetDiagnostics().TotalLeases, Is.EqualTo(1));
            list.Dispose();
            Assert.That(item == null, Is.True);
            Assert.That(resources.GetDiagnostics().TotalLeases, Is.Zero);
            resources.TrimUnused();
            Assert.That(provider.Released, Is.EqualTo(3));
            Assert.That(provider.Duplicates, Is.Zero);
            Assert.That(resources.IsShutDown, Is.False);
            yield return Await(resources.ShutdownAsync().AsTask());
            provider.DestroySprites();
        }

        [Test]
        public void LegacyItemEndsPreviousBindingEvenWithoutNativeDeactivation()
        {
            var root = new GameObject("LegacyItem", typeof(RectTransform), typeof(LegacyListItemProbe));
            try
            {
                var item = root.GetComponent<LegacyListItemProbe>();
                item.BindIndex(0);
                item.BindIndex(1);
                Assert.That(item.Unbinds, Is.EqualTo(1));
                Assert.That(item.Index, Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void NestedDragRoutesOnePairedGestureAndCancelsOnDisable()
        {
            var events = new GameObject("EventSystem", typeof(EventSystem));
            var parent = new GameObject("Outer", typeof(RectTransform), typeof(RecordingScrollRect));
            var child = new GameObject("Inner", typeof(RectTransform), typeof(UINestedScrollRect));
            child.transform.SetParent(parent.transform);
            var outer = parent.GetComponent<RecordingScrollRect>();
            outer.horizontal = true; outer.vertical = false;
            var inner = child.GetComponent<UINestedScrollRect>();
            inner.horizontal = false; inner.vertical = true;
            inner.SetParentScroll(outer);
            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(child.transform);
            inner.content = (RectTransform)content.transform;
            outer.content = (RectTransform)child.transform;
            var data = new PointerEventData(events.GetComponent<EventSystem>())
            { button = PointerEventData.InputButton.Left, pointerId = 3, pressPosition = Vector2.zero, position = new Vector2(100, 2) };
            try
            {
                ExecuteEvents.Execute(child, data, ExecuteEvents.beginDragHandler);
                ExecuteEvents.Execute(child, data, ExecuteEvents.dragHandler);
                child.SetActive(false);
                ExecuteEvents.Execute(child, data, ExecuteEvents.endDragHandler);
                Assert.That(outer.Begins, Is.EqualTo(1));
                outer.vertical = true;
                data.position = new Vector2(1, 100);
                ExecuteEvents.Execute(child, data, ExecuteEvents.beginDragHandler);
                ExecuteEvents.Execute(child, data, ExecuteEvents.dragHandler);
                ExecuteEvents.Execute(child, data, ExecuteEvents.endDragHandler);
                Assert.That(outer.Begins, Is.EqualTo(1), "Same-axis nesting remains owned by the child.");
                Assert.That(outer.Drags, Is.EqualTo(1));
                Assert.That(outer.Ends, Is.EqualTo(1));
                child.SetActive(true);
                data.position = new Vector2(1, 100);
                ExecuteEvents.Execute(child, data, ExecuteEvents.beginDragHandler);
                ExecuteEvents.Execute(child, data, ExecuteEvents.endDragHandler);
                Assert.That(outer.Begins, Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(parent); Object.DestroyImmediate(events); }
        }
        private static IEnumerator Await(Task task)
        {
            for (var frame = 0; frame < 120 && !task.IsCompleted; frame++) yield return null;
            Assert.That(task.IsCompleted, Is.True, "Bounded list test timed out.");
            if (task.IsFaulted) throw task.Exception;
            Assert.That(task.IsCanceled, Is.False);
        }
        private sealed class ListPage : BasePageContext
        {
            public static UIListDataSource<string> Source;
            public static GameObject Prefab;
            public static int Binds;
            public static ListPage Last;
            public SuperScrollViewList<string> List;
            protected override void HandleInit()
            {
                Last = this;
                List = new SuperScrollViewList<string>(View.RectTransform, Source,
                    new SuperScrollViewOptions(), Prefab, (_, __) => Binds++);
                TrackBinding(List);
            }
            protected override void HandleShow(object args) => TrackDisplayBinding(List.BeginDisplay(this));
        }
        private sealed class CoverPage : BasePageContext { }
        private sealed class CancelHide : IUITransition
        {
            public bool Started;
            public UniTask PlayShowAsync(RectTransform target, UITransitionOptions options, CancellationToken cancellationToken = default) => UniTask.CompletedTask;
            public async UniTask PlayHideAsync(RectTransform target, UITransitionOptions options, CancellationToken cancellationToken = default)
            { Started = true; await UniTask.WaitUntilCanceled(cancellationToken); }
        }
        private sealed class PageLoader : IResourceLoader, IDisposable
        {
            private GameObject _page;
            public UniTask<GameObject> LoadPrefabAsync(string key, CancellationToken cancellationToken = default)
            {
                if (_page == null)
                {
                    _page = new GameObject("PagePrefab", typeof(RectTransform), typeof(CanvasGroup), typeof(UIView));
                    ((RectTransform)_page.transform).sizeDelta = new Vector2(500, 400);
                    _page.SetActive(false);
                }
                return UniTask.FromResult(_page);
            }
            public void Release(string key, GameObject instance) { if (instance != null) Object.DestroyImmediate(instance); }
            public void Dispose() { if (_page != null) Object.DestroyImmediate(_page); }
        }
        private sealed class GateProvider : IUIResourceProvider
        {
            private readonly GameObject _prefab;
            private readonly Dictionary<string, UniTaskCompletionSource<IUINativeAssetHandle>> _pending =
                new Dictionary<string, UniTaskCompletionSource<IUINativeAssetHandle>>();
            private readonly Texture2D _texture = new Texture2D(2, 2);
            public readonly Sprite SpriteA;
            public readonly Sprite SpriteB;
            public int Released;
            public int Duplicates;
            public bool DelayPrefab;
            public string PackageName => "test";
            public GateProvider(GameObject prefab)
            {
                _prefab = prefab;
                SpriteA = Sprite.Create(_texture, new Rect(0, 0, 2, 2), Vector2.zero);
                SpriteB = Sprite.Create(_texture, new Rect(0, 0, 1, 1), Vector2.zero);
            }
            public UniTask<IUINativeAssetHandle> LoadAssetAsync(UIResourceKey key)
            {
                if (key.Location == "prefab" && !DelayPrefab) return UniTask.FromResult<IUINativeAssetHandle>(new Handle(this, _prefab));
                var gate = new UniTaskCompletionSource<IUINativeAssetHandle>();
                _pending.Add(key.Location, gate);
                return gate.Task;
            }
            public void Complete(string key) => _pending[key].TrySetResult(new Handle(this, key == "prefab" ? (Object)_prefab : key == "A" ? SpriteA : SpriteB));
            public UniTask ShutdownAsync() => UniTask.CompletedTask;
            public void DestroySprites() { Object.DestroyImmediate(SpriteA); Object.DestroyImmediate(SpriteB); Object.DestroyImmediate(_texture); }
            private sealed class Handle : IUINativeAssetHandle
            {
                private readonly GateProvider _owner;
                public Handle(GateProvider owner, Object asset) { _owner = owner; Asset = asset; }
                public Object Asset { get; private set; }
                public bool IsValid => Asset != null;
                public void Release() { if (!IsValid) _owner.Duplicates++; else _owner.Released++; Asset = null; }
            }
        }
        private sealed class NonCooperativeService : IUIResourceService
        {
            private readonly IUIResourceService _inner;
            public NonCooperativeService(IUIResourceService inner) { _inner = inner; }
            public IUIResourcePackageRegistry Packages => _inner.Packages;
            public bool IsShutDown => _inner.IsShutDown;
            public UniTask<IUIAssetLease<TAsset>> LoadAssetAsync<TAsset>(UIResourceKey key, CancellationToken cancellationToken = default) where TAsset : Object =>
                _inner.LoadAssetAsync<TAsset>(key);
            public UniTask<IUIAssetLease<TAsset>> LoadAssetAsync<TAsset>(string location, string packageName = null, CancellationToken cancellationToken = default) where TAsset : Object =>
                _inner.LoadAssetAsync<TAsset>(location, packageName);
            public UniTask<IUIAssetLease> LoadAssetAsync(UIResourceKey key, CancellationToken cancellationToken = default) => _inner.LoadAssetAsync(key);
            public UniTask<IUIInstanceLease> InstantiateAsync(UIResourceKey key, Transform parent = null, CancellationToken cancellationToken = default) => _inner.InstantiateAsync(key, parent);
            public UniTask<int> PreloadAsync(IEnumerable<UIResourceKey> keys, CancellationToken cancellationToken = default) => _inner.PreloadAsync(keys);
            public UniTask<UIResourceBatchResult> LoadBatchAsync(IEnumerable<UIResourceKey> keys, CancellationToken cancellationToken = default) => _inner.LoadBatchAsync(keys);
            public bool ReleaseUnused(UIResourceKey key) => _inner.ReleaseUnused(key);
            public int TrimUnused() => _inner.TrimUnused();
            public int HandleLowMemory() => _inner.HandleLowMemory();
            public UIResourceDiagnosticsSnapshot GetDiagnostics() => _inner.GetDiagnostics();
            public UniTask ShutdownAsync() => _inner.ShutdownAsync();
        }
    }
}
