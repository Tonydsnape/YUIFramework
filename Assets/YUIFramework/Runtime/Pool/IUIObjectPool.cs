using System;

namespace YUIFramework
{
    /// <summary>
    /// UI 对象池接口。
    /// </summary>
    public interface IUIObjectPool
    {
        bool TryGet(Type contextType, out UIPooledObject pooledObject);
        bool TryGet(
            Type contextType,
            UIPoolScope scope,
            Action<UIPooledObject> invalidAction,
            out UIPooledObject pooledObject);
        bool TryRelease(Type contextType, UIPooledObject pooledObject, UIPoolPolicy policy, out UIPooledObject overflowObject);
        bool TryRelease(
            Type contextType,
            UIPooledObject pooledObject,
            UIPoolPolicy policy,
            UIPoolScope scope,
            string leaseSource,
            bool hasResourceLease,
            out UIPooledObject overflowObject,
            out UIPoolReturnRejection rejection);
        void Clear(Action<UIPooledObject> destroyAction = null);
        void Clear(Type contextType, Action<UIPooledObject> destroyAction = null);
        int ClearScope(UIPoolScope scope, Action<UIPooledObject> destroyAction = null);
        int EvictExpired(Action<UIPooledObject> destroyAction = null);
        int EvictIdle(Action<UIPooledObject> destroyAction = null);
        bool Remove(UIPooledObject pooledObject);
        int RemoveInvalid(Action<UIPooledObject> removeAction = null);
        int Count(Type contextType);
        int Count(Type contextType, UIPoolScope scope);
        UIPoolDiagnosticsSnapshot GetDiagnostics();
    }
}
