namespace YUIFramework
{
    /// <summary>
    /// Stable ten-layer UI model. Legacy names are aliases and share the same runtime state.
    /// </summary>
    public enum UILayer
    {
        Scene = 0,
        Background = 100,
        Normal = 200,
        Fixed = 300,
        Popup = 400,
        Guide = 500,
        Toast = 600,
        Loading = 650,
        System = 700,
        Debug = 800,

        [System.Obsolete("Use Background. Bottom is a compatibility alias.")]
        Bottom = Background,

        [System.Obsolete("Use Toast. Top is a compatibility alias.")]
        Top = Toast,
    }
}
