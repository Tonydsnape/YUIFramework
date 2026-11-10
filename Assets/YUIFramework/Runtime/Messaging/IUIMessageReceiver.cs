using System;

namespace YUIFramework
{
    [Obsolete("Use IUIMessageBus with UIMessageTopic<T>. Scheduled removal: 2.1.0.")]
    public interface IUIMessageReceiver
    {
        void OnMessage(string messageName, object payload);
    }
}
