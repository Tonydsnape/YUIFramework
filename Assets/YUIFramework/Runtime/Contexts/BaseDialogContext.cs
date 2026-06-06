namespace YUIFramework
{
    public abstract class BaseDialogContext : BaseContext
    {
        public override UILayer DefaultLayer => UILayer.Popup;

        /// <summary>
        /// 预留：后续弹窗遮罩策略。
        /// </summary>
        public virtual bool UseMask => true;

        /// <summary>
        /// 预留：后续支持点击遮罩关闭。
        /// </summary>
        public virtual bool CloseOnMaskClick => false;
    }
}
