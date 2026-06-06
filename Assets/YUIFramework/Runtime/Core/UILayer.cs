namespace YUIFramework
{
    /// <summary>
    /// UI 分层定义。
    /// </summary>
    public enum UILayer
    {
        Scene = 0,
        Bottom = 100,

        /// <summary>
        /// 普通全屏页面层，后续导航栈主要工作层。
        /// </summary>
        Normal = 200,

        /// <summary>
        /// 常驻 HUD / 固定挂件层。
        /// </summary>
        Fixed = 300,

        /// <summary>
        /// 弹窗层。
        /// </summary>
        Popup = 400,

        Guide = 500,
        Top = 600,

        /// <summary>
        /// Loading / 断线重连等最高优先级系统层。
        /// </summary>
        System = 700,
    }
}
