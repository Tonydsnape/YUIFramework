using System;
using YUIFramework.Bootstrap;

namespace YUIFramework.HotUpdate
{
    [Obsolete("Use YUIFramework.Bootstrap.BootstrapMode. Scheduled removal: 2.1.0.")]
    public enum HotUpdatePlayMode
    {
        EditorSimulate = BootstrapMode.EditorSimulate,
        Offline = BootstrapMode.Offline,
        Host = BootstrapMode.Host,
    }
}
