using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace YUIFramework
{
    public sealed class UISortingLease : IDisposable
    {
        private UILayerManager _owner;

        internal UISortingLease(UILayerManager owner, UILayer layer, RectTransform view)
        {
            _owner = owner;
            Layer = layer;
            View = view;
        }

        public UILayer Layer { get; }
        public RectTransform View { get; }
        public int SortingOrder { get; internal set; }
        public bool IsDisposed => _owner == null;

        public void Dispose()
        {
            var owner = _owner;
            if (owner == null)
            {
                return;
            }

            _owner = null;
            owner.Release(this);
        }
    }

    /// <summary>
    /// Owns layer roots and compact, bounded sorting leases.
    /// </summary>
    public sealed class UILayerManager : IDisposable
    {
        private readonly UIRoot _uiRoot;
        private readonly UILayerProfile _profile;
        private readonly Dictionary<UILayer, List<UISortingLease>> _leases =
            new Dictionary<UILayer, List<UISortingLease>>();
        private readonly Dictionary<RectTransform, UISortingLease> _byView =
            new Dictionary<RectTransform, UISortingLease>();
        private bool _disposed;

        public UILayerManager(UIRoot uiRoot, UILayerProfile profile)
        {
            _uiRoot = uiRoot ?? throw new ArgumentNullException(nameof(uiRoot));
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            foreach (var descriptor in profile.Descriptors)
            {
                _leases.Add(descriptor.Layer, new List<UISortingLease>());
            }
        }

        [Obsolete("Use UILayerManager(UIRoot, UILayerProfile).")]
        public UILayerManager(UIRoot uiRoot)
            : this(uiRoot, UILayerProfile.CreateDefault())
        {
        }

        public int ActiveLeaseCount => _byView.Count;
        internal UILayerProfile Profile => _profile;

        public RectTransform GetLayer(UILayer layer)
        {
            ThrowIfDisposed();
            return _uiRoot.GetLayerRoot(UILayerProfile.Normalize(layer));
        }

        public int GetActiveLeaseCount(UILayer layer)
        {
            ThrowIfDisposed();
            return _leases[UILayerProfile.Normalize(layer)].Count;
        }

        public int GetPosition(UISortingLease lease)
        {
            ThrowIfDisposed();
            ValidateLease(lease);
            return _leases[lease.Layer].IndexOf(lease);
        }

        public void RestorePosition(UISortingLease lease, int position)
        {
            ThrowIfDisposed();
            ValidateLease(lease);
            var list = _leases[lease.Layer];
            if (!list.Remove(lease))
            {
                throw new InvalidOperationException("The sorting lease is not active.");
            }

            position = Math.Max(0, Math.Min(position, list.Count));
            list.Insert(position, lease);
            if (lease.View != null)
            {
                lease.View.SetSiblingIndex(position);
            }

            Reindex(lease.Layer);
        }

        public UISortingLease AddToLayer(UILayer layer, RectTransform view)
        {
            ThrowIfDisposed();
            if (view == null)
            {
                throw new ArgumentNullException(nameof(view));
            }

            layer = UILayerProfile.Normalize(layer);
            var descriptor = _profile.Get(layer);
            var list = _leases[layer];
            if (_byView.TryGetValue(view, out var existing))
            {
                if (existing.Layer == layer)
                {
                    BringToTop(existing);
                    return existing;
                }
            }

            if (list.Count >= descriptor.Capacity)
            {
                throw new InvalidOperationException(
                    $"Layer {layer} sorting capacity {descriptor.Capacity} is exhausted.");
            }

            var previousParent = view.parent;
            var previousSibling = view.GetSiblingIndex();
            try
            {
                view.SetParent(GetLayer(layer), false);
                view.SetAsLastSibling();
                EnsureViewComponents(view);
            }
            catch
            {
                if (view != null)
                {
                    view.SetParent(previousParent, false);
                    if (previousParent != null)
                    {
                        view.SetSiblingIndex(previousSibling);
                    }
                }

                throw;
            }

            existing?.Dispose();

            var lease = new UISortingLease(this, layer, view);
            list.Add(lease);
            _byView.Add(view, lease);
            Reindex(layer);
            return lease;
        }

        public void BringToTop(UISortingLease lease)
        {
            ThrowIfDisposed();
            ValidateLease(lease);
            var list = _leases[lease.Layer];
            var index = list.IndexOf(lease);
            if (index < 0)
            {
                throw new InvalidOperationException("The sorting lease is not active.");
            }

            if (index != list.Count - 1)
            {
                list.RemoveAt(index);
                list.Add(lease);
            }

            if (lease.View != null)
            {
                lease.View.SetAsLastSibling();
            }

            Reindex(lease.Layer);
        }

        internal void Release(UISortingLease lease)
        {
            if (_disposed || lease == null)
            {
                return;
            }

            var list = _leases[lease.Layer];
            list.Remove(lease);
            if (lease.View != null)
            {
                _byView.Remove(lease.View);
            }

            Reindex(lease.Layer);
        }

        public void SetLayerRaycast(UILayer layer, bool enabled)
        {
            var descriptor = _profile.Get(layer);
            var raycaster = GetLayer(layer).GetComponent<GraphicRaycaster>();
            raycaster.enabled = descriptor.Interactable && enabled;
        }

        public void SetViewRaycast(UISortingLease lease, bool enabled)
        {
            if (lease?.View == null)
            {
                return;
            }

            lease.View.GetComponent<GraphicRaycaster>().enabled = enabled;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var pair in _leases)
            {
                foreach (var lease in pair.Value)
                {
                    if (lease.View != null)
                    {
                        var canvas = lease.View.GetComponent<Canvas>();
                        if (canvas != null)
                        {
                            canvas.overrideSorting = false;
                        }
                    }
                }

                pair.Value.Clear();
            }

            _byView.Clear();
        }

        private void Reindex(UILayer layer)
        {
            var descriptor = _profile.Get(layer);
            var list = _leases[layer];
            for (var i = 0; i < list.Count; i++)
            {
                var lease = list[i];
                var order = descriptor.SortingBase + (i + 1) * 2;
                lease.SortingOrder = order;
                if (lease.View == null)
                {
                    continue;
                }

                var canvas = lease.View.GetComponent<Canvas>();
                canvas.overrideSorting = true;
                canvas.sortingOrder = order;
            }
        }

        private static void EnsureViewComponents(RectTransform view)
        {
            var canvas = view.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = view.gameObject.AddComponent<Canvas>();
            }

            canvas.overrideSorting = true;
            if (view.GetComponent<GraphicRaycaster>() == null)
            {
                view.gameObject.AddComponent<GraphicRaycaster>();
            }
        }

        private void ValidateLease(UISortingLease lease)
        {
            if (lease == null)
            {
                throw new ArgumentNullException(nameof(lease));
            }

            if (lease.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(lease));
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(UILayerManager));
            }

            if (_uiRoot == null)
            {
                throw new UIRootUnavailableException(
                "The UIRoot was destroyed while its runtime was active.");
            }
        }
    }
}
