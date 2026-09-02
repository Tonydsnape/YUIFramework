namespace YUIFramework
{
    /// <summary>
    /// 绑定模式。
    /// </summary>
    public enum BindingMode
    {
        /// <summary>
        /// 单向绑定：ViewModel -> View。
        /// </summary>
        OneWay,

        /// <summary>
        /// 双向绑定：ViewModel <-> View。
        /// </summary>
        TwoWay,

        /// <summary>
        /// 一次性绑定：仅绑定时同步一次，不持续监听。
        /// </summary>
        OneTime,
    }
}
