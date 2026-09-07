using System;
using YUIFramework.Bootstrap;

namespace YUIFramework.HotUpdate
{
    [Obsolete("Use BootstrapProgressUI as an injected IBootstrapProgressSink.")]
    public sealed class HotUpdateProgressUI : BootstrapProgressUI
    {
        protected override void OnEnable()
        {
            base.OnEnable();
            HotUpdateLauncher.OnProgress += SetLegacyProgress;
            HotUpdateLauncher.OnStatus += SetLegacyStatus;
        }

        private void OnDisable()
        {
            HotUpdateLauncher.OnProgress -= SetLegacyProgress;
            HotUpdateLauncher.OnStatus -= SetLegacyStatus;
        }
    }
}
