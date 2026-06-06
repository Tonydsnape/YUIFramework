namespace YUIFramework
{
    public abstract class BasePageContext : BaseContext
    {
        public override UILayer DefaultLayer => UILayer.Normal;

        /// <summary>
        /// 预留：后续导航栈可读取该标记作为全屏遮挡策略依据。
        /// </summary>
        public virtual bool IsFullScreen => true;
    }
}
