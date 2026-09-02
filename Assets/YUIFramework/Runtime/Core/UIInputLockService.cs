using System;
using System.Collections.Generic;
using System.Linq;

namespace YUIFramework
{
    public sealed class UIInputLockInfo
    {
        internal UIInputLockInfo(long id, object owner, string reason, HashSet<UILayer> allowedLayers)
        {
            Id = id;
            Owner = owner;
            Reason = reason;
            AllowedLayers = allowedLayers;
        }

        public long Id { get; }
        public object Owner { get; }
        public string Reason { get; }
        public IReadOnlyCollection<UILayer> AllowedLayers { get; }
    }

    public sealed class UIInputLockService : IDisposable
    {
        private readonly UIInteractionController _interaction;
        private readonly Dictionary<long, UIInputLockInfo> _locks =
            new Dictionary<long, UIInputLockInfo>();
        private long _nextId;
        private bool _disposed;

        internal UIInputLockService(
            UILayerProfile profile,
            UIInteractionController interaction)
        {
            _ = profile ?? throw new ArgumentNullException(nameof(profile));
            _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        }

        public int ActiveLockCount => _locks.Count;
        public bool IsLocked => _locks.Count > 0;
        public IReadOnlyCollection<UIInputLockInfo> ActiveLocks =>
            new List<UIInputLockInfo>(_locks.Values);

        public IDisposable Acquire(object owner, string reason, params UILayer[] allowedLayers)
        {
            ThrowIfDisposed();
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("An input lock reason is required.", nameof(reason));
            }

            var allowed = new HashSet<UILayer>();
            if (allowedLayers != null)
            {
                foreach (var layer in allowedLayers)
                {
                    allowed.Add(UILayerProfile.Normalize(layer));
                }
            }

            var id = ++_nextId;
            _locks.Add(id, new UIInputLockInfo(id, owner, reason, allowed));
            Apply();
            return new Lease(this, id);
        }

        public bool IsLayerAllowed(UILayer layer)
        {
            layer = UILayerProfile.Normalize(layer);
            if (_locks.Count == 0)
            {
                return true;
            }

            foreach (var inputLock in _locks.Values)
            {
                if (!inputLock.AllowedLayers.Contains(layer))
                {
                    return false;
                }
            }

            return true;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _locks.Clear();
        }

        private void Release(long id)
        {
            if (_disposed)
            {
                return;
            }

            if (_locks.Remove(id))
            {
                Apply();
            }
        }

        private void Apply()
        {
            _interaction.Apply();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(UIInputLockService));
            }
        }

        private sealed class Lease : IDisposable
        {
            private UIInputLockService _owner;
            private readonly long _id;

            public Lease(UIInputLockService owner, long id)
            {
                _owner = owner;
                _id = id;
            }

            public void Dispose()
            {
                var owner = _owner;
                if (owner == null)
                {
                    return;
                }

                _owner = null;
                owner.Release(_id);
            }
        }
    }
}
