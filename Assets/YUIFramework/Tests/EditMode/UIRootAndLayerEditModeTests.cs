using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace YUIFramework.Tests
{
    public sealed class UIRootAndLayerEditModeTests
    {
        [Test]
        public void DefaultProfile_ContainsCanonicalTenLayersInOrder()
        {
            var profile = UILayerProfile.CreateDefault();
            var expected = new[]
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

            Assert.That(profile.Descriptors.Count, Is.EqualTo(expected.Length));
            for (var i = 0; i < expected.Length; i++)
            {
                Assert.That(profile.Descriptors[i].Layer, Is.EqualTo(expected[i]));
                Assert.That(profile.GetIndex(expected[i]), Is.EqualTo(i));
            }
        }

        [Test]
        public void LegacyLayerAliases_ResolveToCanonicalState()
        {
#pragma warning disable CS0618
            Assert.That(UILayerProfile.Normalize(UILayer.Bottom), Is.EqualTo(UILayer.Background));
            Assert.That(UILayerProfile.Normalize(UILayer.Top), Is.EqualTo(UILayer.Toast));
#pragma warning restore CS0618
            Assert.That((int)UILayer.Scene, Is.EqualTo(0));
            Assert.That((int)UILayer.Normal, Is.EqualTo(200));
            Assert.That((int)UILayer.System, Is.EqualTo(700));
        }

        [Test]
        public void Profile_RejectsDuplicateUnknownAndOverlappingRanges()
        {
            var duplicate = CopyDefault();
            duplicate[1] = CopyWith(
                duplicate[1],
                layer: UILayer.Background);
            Assert.Throws<ArgumentException>(() => new UILayerProfile(duplicate));

            var unknown = CopyDefault();
            unknown[9] = CopyWith(
                unknown[9],
                layer: (UILayer)12345);
            Assert.Throws<ArgumentException>(() => new UILayerProfile(unknown));

            var overlap = CopyDefault();
            overlap[1] = CopyWith(
                overlap[1],
                sortingBase: overlap[0].SortingBase + 1);
            Assert.Throws<ArgumentException>(() => new UILayerProfile(overlap));
        }

        [Test]
        public void Profile_RejectsSortingRangePastCanvasLimit()
        {
            var descriptors = CopyDefault();
            descriptors[9] = CopyWith(
                descriptors[9],
                sortingBase: short.MaxValue - 1);
            Assert.Throws<ArgumentOutOfRangeException>(() => new UILayerProfile(descriptors));
        }

        private static List<UILayerDescriptor> CopyDefault()
        {
            var result = new List<UILayerDescriptor>();
            foreach (var item in UILayerProfile.CreateDefault().Descriptors)
            {
                result.Add(new UILayerDescriptor(
                    item.Layer,
                    item.SortingBase,
                    item.Capacity,
                    item.Interactable,
                    item.Modal));
            }

            return result;
        }

        private static UILayerDescriptor CopyWith(
            UILayerDescriptor source,
            UILayer? layer = null,
            int? sortingBase = null)
        {
            return new UILayerDescriptor(
                layer ?? source.Layer,
                sortingBase ?? source.SortingBase,
                source.Capacity,
                source.Interactable,
                source.Modal);
        }
    }
}
