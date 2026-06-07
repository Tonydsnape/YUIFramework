using System;

namespace YUIFramework
{
    /// <summary>
    /// 数据绑定异常。
    /// </summary>
    public sealed class BindingException : Exception
    {
        public BindingException(string message)
            : base(message)
        {
        }

        public BindingException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
