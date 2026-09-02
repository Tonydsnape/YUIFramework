namespace YUIFramework
{
    public enum UIContextState
    {
        Unloaded = 0,
        None = Unloaded,
        Loading = 1,
        Initializing = 2,
        Opening = 3,
        Opened = 4,
        Shown = Opened,
        Hiding = 5,
        Hidden = 6,
        Closing = 7,
        Pooled = 8,
        Closed = Pooled,
        Releasing = 9,
        Released = 10,
        Destroyed = Released,
        Faulted = 11
    }
}
