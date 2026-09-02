namespace YUIFramework
{
    public static class UIContextStateGraph
    {
        public static bool CanTransition(UIContextState from, UIContextState to)
        {
            if (from == to)
            {
                return true;
            }

            switch (from)
            {
                case UIContextState.Unloaded:
                    return to == UIContextState.Loading ||
                           to == UIContextState.Releasing ||
                           to == UIContextState.Faulted;
                case UIContextState.Loading:
                    return to == UIContextState.Initializing ||
                           to == UIContextState.Releasing ||
                           to == UIContextState.Faulted;
                case UIContextState.Initializing:
                    return to == UIContextState.Opening ||
                           to == UIContextState.Releasing ||
                           to == UIContextState.Faulted;
                case UIContextState.Opening:
                    return to == UIContextState.Opened ||
                           to == UIContextState.Hiding ||
                           to == UIContextState.Hidden ||
                           to == UIContextState.Pooled ||
                           to == UIContextState.Releasing ||
                           to == UIContextState.Faulted;
                case UIContextState.Opened:
                    return to == UIContextState.Opening ||
                           to == UIContextState.Hiding ||
                           to == UIContextState.Closing ||
                           to == UIContextState.Releasing ||
                           to == UIContextState.Faulted;
                case UIContextState.Hiding:
                    return to == UIContextState.Hidden ||
                           to == UIContextState.Opened ||
                           to == UIContextState.Releasing ||
                           to == UIContextState.Faulted;
                case UIContextState.Hidden:
                    return to == UIContextState.Opening ||
                           to == UIContextState.Closing ||
                           to == UIContextState.Releasing ||
                           to == UIContextState.Faulted;
                case UIContextState.Closing:
                    return to == UIContextState.Pooled ||
                           to == UIContextState.Releasing ||
                           to == UIContextState.Faulted;
                case UIContextState.Pooled:
                    return to == UIContextState.Opening ||
                           to == UIContextState.Releasing ||
                           to == UIContextState.Faulted;
                case UIContextState.Faulted:
                    return to == UIContextState.Releasing;
                case UIContextState.Releasing:
                    return to == UIContextState.Released;
                case UIContextState.Released:
                    return false;
                default:
                    return false;
            }
        }
    }
}
