using System;
using Cysharp.Threading.Tasks;

namespace YUIFramework.Bootstrap.YooAsset
{
    public sealed class YooAssetBootstrapComposition
    {
        private YooAssetBootstrapComposition(
            BootstrapReadyContext readyContext,
            IUIResourceService resourceService)
        {
            ReadyContext = readyContext;
            ResourceService = resourceService;
        }

        public BootstrapReadyContext ReadyContext { get; }

        public IUIResourceService ResourceService { get; }

        public static YooAssetBootstrapComposition Create(BootstrapReadyContext readyContext)
        {
            if (readyContext == null)
            {
                throw new ArgumentNullException(nameof(readyContext));
            }

            var resourceService = new UIResourceService();
            for (var i = 0; i < readyContext.Packages.Count; i++)
            {
                if (!(readyContext.Packages[i] is YooAssetBootstrapPackageHandle package))
                {
                    throw new ArgumentException(
                        "Ready context contains a package from a non-YooAsset backend.",
                        nameof(readyContext));
                }

                resourceService.Packages.Register(
                    new YooAssetResourceProvider(package.Package),
                    isDefault: i == 0);
            }

            return new YooAssetBootstrapComposition(readyContext, resourceService);
        }

        public UniTask ShutdownResourceServiceAsync()
        {
            // Provider handles must be released before the runner destroys its packages.
            return ResourceService.ShutdownAsync();
        }
    }
}
