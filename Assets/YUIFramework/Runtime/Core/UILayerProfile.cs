using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace YUIFramework
{
    [Serializable]
    public sealed class UILayerDescriptor
    {
        public UILayerDescriptor(
            UILayer layer,
            int sortingBase,
            int capacity = 128,
            bool interactable = true,
            bool modal = false)
        {
            Layer = layer;
            SortingBase = sortingBase;
            Capacity = capacity;
            Interactable = interactable;
            Modal = modal;
        }

        public UILayer Layer { get; }
        public int SortingBase { get; }
        public int Capacity { get; }
        public bool Interactable { get; }
        public bool Modal { get; }
    }

    [Serializable]
    public sealed class UILayerProfile
    {
        private static readonly UILayer[] RequiredLayers =
        {
            UILayer.Background,
            UILayer.Scene,
            UILayer.Normal,
            UILayer.Fixed,
            UILayer.Popup,
            UILayer.Guide,
            UILayer.Toast,
            UILayer.Loading,
            UILayer.System,
            UILayer.Debug,
        };

        private readonly ReadOnlyCollection<UILayerDescriptor> _descriptors;
        private readonly Dictionary<UILayer, UILayerDescriptor> _byLayer;

        public UILayerProfile(IEnumerable<UILayerDescriptor> descriptors)
        {
            if (descriptors == null)
            {
                throw new ArgumentNullException(nameof(descriptors));
            }

            var copied = new List<UILayerDescriptor>();
            foreach (var descriptor in descriptors)
            {
                if (descriptor == null)
                {
                    copied.Add(null);
                    continue;
                }

                copied.Add(new UILayerDescriptor(
                    Normalize(descriptor.Layer),
                    descriptor.SortingBase,
                    descriptor.Capacity,
                    descriptor.Interactable,
                    descriptor.Modal));
            }

            _descriptors = copied.AsReadOnly();
            _byLayer = ValidateAndIndex(_descriptors);
        }

        public IReadOnlyList<UILayerDescriptor> Descriptors => _descriptors;

        public UILayerDescriptor Get(UILayer layer)
        {
            layer = Normalize(layer);
            if (_byLayer.TryGetValue(layer, out var descriptor))
            {
                return descriptor;
            }

            throw new ArgumentOutOfRangeException(nameof(layer), layer, "Unknown UI layer.");
        }

        public int GetIndex(UILayer layer)
        {
            var descriptor = Get(layer);
            for (var i = 0; i < _descriptors.Count; i++)
            {
                if (ReferenceEquals(_descriptors[i], descriptor))
                {
                    return i;
                }
            }

            throw new InvalidOperationException($"Layer {layer} is not indexed.");
        }

        public static UILayerProfile CreateDefault()
        {
            return new UILayerProfile(new[]
            {
                new UILayerDescriptor(UILayer.Background, 0),
                new UILayerDescriptor(UILayer.Scene, 1000),
                new UILayerDescriptor(UILayer.Normal, 2000),
                new UILayerDescriptor(UILayer.Fixed, 3000),
                new UILayerDescriptor(UILayer.Popup, 4000, modal: true),
                new UILayerDescriptor(UILayer.Guide, 5000),
                new UILayerDescriptor(UILayer.Toast, 6000),
                new UILayerDescriptor(UILayer.Loading, 7000, modal: true),
                new UILayerDescriptor(UILayer.System, 8000, modal: true),
                new UILayerDescriptor(UILayer.Debug, 9000),
            });
        }

        public static UILayer Normalize(UILayer layer)
        {
#pragma warning disable CS0618
            if (layer == UILayer.Bottom)
            {
                return UILayer.Background;
            }

            if (layer == UILayer.Top)
            {
                return UILayer.Toast;
            }
#pragma warning restore CS0618
            return layer;
        }

        private static Dictionary<UILayer, UILayerDescriptor> ValidateAndIndex(
            IReadOnlyList<UILayerDescriptor> descriptors)
        {
            if (descriptors.Count != RequiredLayers.Length)
            {
                throw new ArgumentException(
                    $"A layer profile must contain exactly {RequiredLayers.Length} descriptors.",
                    nameof(descriptors));
            }

            var result = new Dictionary<UILayer, UILayerDescriptor>();
            var previousEnd = int.MinValue;
            for (var i = 0; i < descriptors.Count; i++)
            {
                var descriptor = descriptors[i] ??
                    throw new ArgumentException($"Layer descriptor {i} is null.", nameof(descriptors));
                var normalized = Normalize(descriptor.Layer);
                if (normalized != RequiredLayers[i])
                {
                    throw new ArgumentException(
                        $"Layer descriptor {i} must be {RequiredLayers[i]}, but was {descriptor.Layer}.",
                        nameof(descriptors));
                }

                if (descriptor.Capacity < 2)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(descriptors),
                        $"Layer {normalized} capacity must be at least 2.");
                }

                var end = (long)descriptor.SortingBase + descriptor.Capacity * 2L;
                if (end > short.MaxValue || descriptor.SortingBase < short.MinValue)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(descriptors),
                        $"Layer {normalized} sorting range exceeds Canvas sorting limits.");
                }

                if (descriptor.SortingBase <= previousEnd)
                {
                    throw new ArgumentException(
                        $"Layer {normalized} sorting range overlaps or is not strictly ordered.",
                        nameof(descriptors));
                }

                result.Add(normalized, descriptor);
                previousEnd = (int)end;
            }

            return result;
        }
    }
}
