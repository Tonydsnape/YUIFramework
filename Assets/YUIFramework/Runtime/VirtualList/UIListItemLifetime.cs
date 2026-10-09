using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace YUIFramework
{
    public sealed class UIListItemLifetime : MonoBehaviour
    {
        private long _generation;
        private CancellationTokenSource _cancellation;
        private List<IDisposable> _resources;
        private string _id;
        private bool _bound;
        public int Index { get; private set; } = -1;
        public string Id => _id;
        public bool IsBound => _bound;
        public UIListItemBinding BeginBind(string id, int index, bool selected)
        {
            EndBind();
            _id = id;
            Index = index;
            _bound = true;
            return new UIListItemBinding(this, _generation, id, index, selected);
        }
        internal bool IsCurrent(long generation, string id) =>
            this != null && _bound && _generation == generation && string.Equals(_id, id, StringComparison.Ordinal);
        internal CancellationToken GetToken(long generation, string id)
        {
            if (!IsCurrent(generation, id)) return new CancellationToken(true);
            _cancellation ??= new CancellationTokenSource();
            return _cancellation.Token;
        }
        internal void Track(IDisposable resource) => (_resources ??= new List<IDisposable>()).Add(resource);
        public void EndBind()
        {
            _generation++;
            _bound = false;
            _id = null;
            Index = -1;
            List<Exception> errors = null;
            var cancellation = _cancellation;
            _cancellation = null;
            try { cancellation?.Cancel(); }
            catch (Exception error) { (errors ??= new List<Exception>()).Add(error); }
            finally { cancellation?.Dispose(); }
            if (_resources != null)
            {
                for (var index = _resources.Count - 1; index >= 0; index--)
                {
                    try { _resources[index].Dispose(); }
                    catch (Exception error) { (errors ??= new List<Exception>()).Add(error); }
                }
                _resources.Clear();
            }
            if (errors != null) throw new AggregateException("List item cleanup failed.", errors);
        }
        private void OnDisable() => EndBind();
        private void OnDestroy() => EndBind();
    }

    public readonly struct UIListItemBinding
    {
        private readonly UIListItemLifetime _owner;
        private readonly long _generation;
        internal UIListItemBinding(UIListItemLifetime owner, long generation, string id, int index, bool selected)
        { _owner = owner; _generation = generation; Id = id; Index = index; IsSelected = selected; }
        public string Id { get; }
        public int Index { get; }
        public bool IsSelected { get; }
        public bool IsCurrent => _owner != null && _owner.IsCurrent(_generation, Id);
        public GameObject View => IsCurrent ? _owner.gameObject : null;
        public CancellationToken Token => _owner == null ? new CancellationToken(true) : _owner.GetToken(_generation, Id);
        public bool TryApply(Action<GameObject> update)
        {
            if (update == null) throw new ArgumentNullException(nameof(update));
            if (!IsCurrent) return false;
            update(_owner.gameObject);
            return true;
        }
        public async UniTask<bool> LoadSpriteAsync(IUIResourceService resources, UIResourceKey key, Image image)
        {
            if (resources == null) throw new ArgumentNullException(nameof(resources));
            if (!IsCurrent || image == null) return false;
            var lease = await resources.LoadAssetAsync<Sprite>(key, Token);
            try
            {
                if (!IsCurrent || image == null) return false;
                image.sprite = lease.Asset;
                _owner.Track(new SpriteBindingLease(image, lease));
                lease = null;
                return true;
            }
            finally { lease?.Dispose(); }
        }
        private sealed class SpriteBindingLease : IDisposable
        {
            private readonly Image _image;
            private IUIAssetLease<Sprite> _lease;
            public SpriteBindingLease(Image image, IUIAssetLease<Sprite> lease) { _image = image; _lease = lease; }
            public void Dispose()
            {
                var lease = _lease;
                _lease = null;
                if (lease == null) return;
                if (_image != null && _image.sprite == lease.Asset) _image.sprite = null;
                lease.Dispose();
            }
        }
    }
}
