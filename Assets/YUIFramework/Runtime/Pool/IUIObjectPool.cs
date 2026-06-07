using System;

namespace YUIFramework
{
    /// <summary>
    /// UI 对象池接口。
    /// </summary>
    public interface IUIObjectPool
    {
        bool TryGet(Type contextType, out UIPooledObject pooledObject);
        bool TryRelease(Type contextType, UIPooledObject pooledObject, UIPoolPolicy policy, out UIPooledObject overflowObject);
        void Clear(Action<UIPooledObject> destroyAction = null);
        void Clear(Type contextType, Action<UIPooledObject> destroyAction = null);
        int Count(Type contextType);
    }
}
