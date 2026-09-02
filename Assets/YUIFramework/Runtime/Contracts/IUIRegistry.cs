using System;

namespace YUIFramework
{
    public interface IUIRegistry
    {
        void Register<T>(UIConfig config) where T : BaseContext;
        bool IsRegistered<T>() where T : BaseContext;
        bool TryGetConfig(Type contextType, out UIConfig config);
    }
}
