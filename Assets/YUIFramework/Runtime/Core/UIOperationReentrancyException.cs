using System;

namespace YUIFramework
{
    /// <summary>
    /// 当生命周期回调（OnShow/OnHide/OnClose 等）或导航守卫在其自身执行期间，
    /// 尝试对同一个 key 再次发起会被同一条 FIFO 队列串行化的操作时抛出。
    /// 这类调用如果被简单地排队等待，会造成“等待自己完成”的死锁；因此在入队时立即
    /// 显式失败，而不是挂起。
    /// </summary>
    public sealed class UIOperationReentrancyException : InvalidOperationException
    {
        public UIOperationReentrancyException(object key, string operationName)
            : base(BuildMessage(key, operationName))
        {
            Key = key;
            OperationName = operationName;
        }

        public object Key { get; }
        public string OperationName { get; }

        private static string BuildMessage(object key, string operationName)
        {
            return
                $"Reentrant '{operationName}' operation detected for key '{key}'. " +
                "A lifecycle callback or navigation guard attempted to invoke an operation " +
                "that is already executing for the same key on the current call chain, " +
                "which would deadlock waiting for itself. Schedule the follow-up work " +
                "outside the callback (for example on the next frame) instead.";
        }
    }
}
