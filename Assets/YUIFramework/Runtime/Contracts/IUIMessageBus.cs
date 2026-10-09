using System;
using System.Threading;

namespace YUIFramework
{
    public interface IUIMessageBus
    {
        int ListenerCount { get; }
        UIMessageScope CreateScope(
            string name = null,
            CancellationToken cancellationToken = default);
        UIMessageToken Subscribe<T>(
            UIMessageTopic<T> topic,
            Action<T> handler,
            UIMessageScope scope = null,
            int priority = 0,
            object owner = null);
        void Publish<T>(UIMessageTopic<T> topic, T payload);
        int Count<T>(UIMessageTopic<T> topic);

        [Obsolete("Use Subscribe(UIMessageTopic<UIMessageUnit>, ...). String message APIs will be removed after the Y2 migration window.")]
        UIMessageToken Subscribe(string messageName, Action handler, object owner = null);
        [Obsolete("Use Subscribe(UIMessageTopic<T>, ...). String message APIs will be removed after the Y2 migration window.")]
        UIMessageToken Subscribe<T>(string messageName, Action<T> handler, object owner = null);
        [Obsolete("Use Publish(UIMessageTopic<UIMessageUnit>, UIMessageUnit.Value). String message APIs will be removed after the Y2 migration window.")]
        void Publish(string messageName);
        [Obsolete("Use Publish(UIMessageTopic<T>, payload). String message APIs will be removed after the Y2 migration window.")]
        void Publish<T>(string messageName, T payload);
        void Unsubscribe(UIMessageToken token);
        void UnsubscribeOwner(object owner);
        void Clear();
        [Obsolete("Use Count(UIMessageTopic<T>). String message APIs will be removed after the Y2 migration window.")]
        int Count(string messageName);
    }
}
