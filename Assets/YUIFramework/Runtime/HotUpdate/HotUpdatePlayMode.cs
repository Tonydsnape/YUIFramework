using System;
using YUIFramework.Bootstrap;

namespace YUIFramework.HotUpdate
{
    [Obsolete("Use YUIFramework.Bootstrap.BootstrapMode.")]
    public enum HotUpdatePlayMode
    {
        EditorSimulate = BootstrapMode.EditorSimulate,
        Offline = BootstrapMode.Offline,
        Host = BootstrapMode.Host,
    }
}
