using System;

namespace YUIFramework
{
    public interface IUIMessageBus
    {
        int ListenerCount { get; }
        UIMessageToken Subscribe(string messageName, Action handler, object owner = null);
        UIMessageToken Subscribe<T>(string messageName, Action<T> handler, object owner = null);
        void Publish(string messageName);
        void Publish<T>(string messageName, T payload);
        void Unsubscribe(UIMessageToken token);
        void UnsubscribeOwner(object owner);
        void Clear();
        int Count(string messageName);
    }
}
