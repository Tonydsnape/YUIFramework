using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using YUIFramework.Bootstrap;

namespace YUIFramework.CodeLoading
{
    public sealed class HotfixGameEntry : IBootstrapGameEntry
    {
        private readonly ResourceHotfixCodeLoader _loader;
        private readonly Func<BootstrapReadyContext, IUIService> _ui;
        private readonly UIConfig _config;
        private bool _entering;
        public BaseContext Page { get; private set; }
        public HotfixGameEntry(ResourceHotfixCodeLoader loader, Func<BootstrapReadyContext, IUIService> ui, UIConfig page)
        {
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _config = (page ?? throw new ArgumentNullException(nameof(page))).Copy();
            UIConfigRegistration.Validate(_config);
        }
        public async UniTask EnterAsync(BootstrapReadyContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_entering || Page != null) throw new InvalidOperationException("A Hotfix entry is in progress or already entered; the owning application must create a new entry for re-entry.");
            var entry = _loader.Entry ?? throw new InvalidOperationException("The code extension has not completed.");
            var ui = _ui(context) ?? throw new InvalidOperationException("AOT UI composition is missing.");
            if (!ui.IsInitialized) throw new InvalidOperationException("The AOT owner must initialize UI before business entry.");
            _entering = true;
            try
            {
                Page = await entry.EnterAsync(ui, _config.Copy(), cancellationToken);
                if (Page == null) throw new InvalidOperationException("Hotfix business entry returned no page.");
            }
            finally { _entering = false; }
        }
    }
}
