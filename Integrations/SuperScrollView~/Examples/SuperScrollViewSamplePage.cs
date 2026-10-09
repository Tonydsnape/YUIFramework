using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace YUIFramework.Integrations.Examples
{
    public sealed class SuperScrollViewSamplePage : BasePageContext
    {
        public sealed class Arguments
        {
            public IUIResourceService Resources;
            public UIResourceKey ItemPrefab;
            public string SpriteLocation;
        }
        public sealed class Row
        {
            public string Id;
            public string Label;
        }
        private readonly ObservableCollection<Row> _rows = new ObservableCollection<Row>();
        private UIListDataSource<Row> _source;
        private UIListSelection _selection;
        private SuperScrollViewList<Row> _list;
        private int _insertId;

        protected override void HandleInit()
        {
            for (var index = 0; index < 10000; index++)
                _rows.Add(new Row { Id = index.ToString(), Label = $"Row {index}" });
            _source = new UIListDataSource<Row>(_rows, row => row.Id);
            _selection = new UIListSelection(_source, multiple: true);
            TrackBinding(_source);
            TrackBinding(_selection);
        }
        protected override void HandleShow(object args)
        {
            if (!(args is Arguments request) || request.Resources == null)
                throw new ArgumentException("Provide the caller-owned YooAsset-backed UIResourceService and item prefab key.");
            RunDisplayTask(async cancellationToken =>
            {
                _list = await SuperScrollViewList<Row>.CreateAsync(View.RectTransform, _source,
                    new SuperScrollViewOptions(), request.Resources, request.ItemPrefab,
                    (binding, row) =>
                    {
                        var cell = binding.View.GetComponent<SuperScrollViewSampleCell>();
                        if (cell == null) throw new InvalidOperationException("The item prefab requires SuperScrollViewSampleCell.");
                        cell.Render(row.Label, binding.IsSelected, () => _selection.Set(row.Id, !_selection.Contains(row.Id)));
                        if (!string.IsNullOrEmpty(request.SpriteLocation))
                            binding.LoadSpriteAsync(request.Resources, UIResourceKey.Of<Sprite>(request.SpriteLocation), cell.Icon)
                                .Forget(error => _list.Report(error));
                    }, _selection, cancellationToken);
                TrackDisplayBinding(_list);
                TrackDisplayBinding(_list.BeginDisplay(this));
            });
        }
        public void InsertFirst() => _rows.Insert(0, new Row { Id = $"new-{++_insertId}", Label = "Inserted" });
        public void RemoveFirst() { if (_rows.Count > 0) _rows.RemoveAt(0); }
        public void MoveFirstToLast() { if (_rows.Count > 1) _rows.Move(0, _rows.Count - 1); }
        public void ScrollToMiddle() => _list.ScrollTo(_rows.Count / 2);
    }
}
