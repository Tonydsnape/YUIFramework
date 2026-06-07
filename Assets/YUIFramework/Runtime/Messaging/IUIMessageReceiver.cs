namespace YUIFramework
{
    public interface IUIMessageReceiver
    {
        void OnMessage(string messageName, object payload);
    }
}
