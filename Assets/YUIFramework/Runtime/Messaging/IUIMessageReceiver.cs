using System;

namespace YUIFramework
{
    [Obsolete("Use IUIMessageBus with UIMessageTopic<T>. This string receiver will be removed after the Y2 migration window.")]
    public interface IUIMessageReceiver
    {
        void OnMessage(string messageName, object payload);
    }
}
