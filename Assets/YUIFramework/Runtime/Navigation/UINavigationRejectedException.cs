using System;

namespace YUIFramework
{
    /// <summary>
    /// 由导航守卫拒绝，或因为一次即将执行的转换（Push/Replace/BringToTop）判定为不可
    /// 安全提交而拒绝时抛出。拒绝发生在命令执行体产生任何副作用之前，导航栈和相关
    /// Context 保持不变。
    /// </summary>
    public sealed class UINavigationRejectedException : InvalidOperationException
    {
        public UINavigationRejectedException(UINavigationRequest request, string reason = null)
            : base(BuildMessage(request, reason))
        {
            Request = request;
        }

        public UINavigationRequest Request { get; }

        private static string BuildMessage(UINavigationRequest request, string reason)
        {
            var message = $"Navigation command {request} was rejected before it made any changes.";
            return string.IsNullOrEmpty(reason) ? message : $"{message} {reason}";
        }
    }
}
