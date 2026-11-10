namespace YUIFramework
{
    public enum UIContextState
    {
        Unloaded = 0,
        [System.Obsolete("Use Unloaded. Scheduled removal: 2.1.0.")]
        None = Unloaded,
        Loading = 1,
        Initializing = 2,
        Opening = 3,
        Opened = 4,
        [System.Obsolete("Use Opened. Scheduled removal: 2.1.0.")]
        Shown = Opened,
        Hiding = 5,
        Hidden = 6,
        Closing = 7,
        Pooled = 8,
        [System.Obsolete("Use Pooled (not Released). Scheduled removal: 2.1.0.")]
        Closed = Pooled,
        Releasing = 9,
        Released = 10,
        [System.Obsolete("Use Released. Scheduled removal: 2.1.0.")]
        Destroyed = Released,
        Faulted = 11
    }
}
