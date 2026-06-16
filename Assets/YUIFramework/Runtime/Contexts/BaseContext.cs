using UnityEngine;

namespace YUIFramework
{
    /// <summary>
    /// UI Context 生命周期基类。
    /// </summary>
    public abstract class BaseContext : IUIContext
    {
        private bool _initialized;

        public string Id { get; internal set; }
        public UILayer Layer { get; internal set; }
        public UIContextState State { get; internal set; }
        public UIView View { get; internal set; }
        public GameObject ViewObject { get; internal set; }
        public abstract UILayer DefaultLayer { get; }

        internal void BindRuntime(string id, UILayer layer, UIView view, GameObject viewObject)
        {
            Id = id;
            Layer = layer;
            View = view;
            ViewObject = viewObject;
            State = UIContextState.None;
        }

        public void OnInit()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            HandleInit();
        }

        public void OnShow(object args)
        {
            State = UIContextState.Shown;
            HandleShow(args);
        }

        public void OnHide()
        {
            State = UIContextState.Hidden;
            HandleHide();
        }

        public void OnClose()
        {
            State = UIContextState.Closed;
            HandleClose();
        }

        public void OnDestroy()
        {
            State = UIContextState.Destroyed;
            HandleDestroy();
        }

        protected virtual void HandleInit()
        {
        }

        protected virtual void HandleShow(object args)
        {
        }

        protected virtual void HandleHide()
        {
        }

        protected virtual void HandleClose()
        {
        }

        protected virtual void HandleDestroy()
        {
        }
    }
}
