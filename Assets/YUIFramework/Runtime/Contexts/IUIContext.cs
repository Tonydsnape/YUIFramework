namespace YUIFramework
{
    public interface IUIContext
    {
        void OnInit();
        void OnShow(object args);
        void OnHide();
        void OnClose();
        void OnDestroy();
    }
}
