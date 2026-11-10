using System;
using YUIFramework.Bootstrap;

namespace YUIFramework.HotUpdate
{
    [Obsolete("Use BootstrapProgressUI as an injected IBootstrapProgressSink. Scheduled removal: 2.1.0.")]
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
