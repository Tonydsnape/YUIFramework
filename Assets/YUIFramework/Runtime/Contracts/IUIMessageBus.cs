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

        [Obsolete("Use Subscribe(UIMessageTopic<UIMessageUnit>, ...). Scheduled removal: 2.1.0.")]
        UIMessageToken Subscribe(string messageName, Action handler, object owner = null);
        [Obsolete("Use Subscribe(UIMessageTopic<T>, ...). Scheduled removal: 2.1.0.")]
        UIMessageToken Subscribe<T>(string messageName, Action<T> handler, object owner = null);
        [Obsolete("Use Publish(UIMessageTopic<UIMessageUnit>, UIMessageUnit.Value). Scheduled removal: 2.1.0.")]
        void Publish(string messageName);
        [Obsolete("Use Publish(UIMessageTopic<T>, payload). Scheduled removal: 2.1.0.")]
        void Publish<T>(string messageName, T payload);
        void Unsubscribe(UIMessageToken token);
        void UnsubscribeOwner(object owner);
        void Clear();
        [Obsolete("Use Count(UIMessageTopic<T>). Scheduled removal: 2.1.0.")]
        int Count(string messageName);
    }
}
