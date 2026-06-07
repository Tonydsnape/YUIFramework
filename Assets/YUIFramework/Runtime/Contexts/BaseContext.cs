using System;
using System.Collections.Generic;
using UnityEngine;

namespace YUIFramework
{
    /// <summary>
    /// UI Context 生命周期基类。
    /// </summary>
    public abstract class BaseContext : IUIContext
    {
        private bool _initialized;
        private readonly List<UIMessageToken> _messageTokens = new List<UIMessageToken>();
        private readonly List<IDisposable> _bindingTokens = new List<IDisposable>();
        private IViewModel _viewModel;

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
            UnsubscribeAllMessages();
            ClearBindings();
            ClearViewModel();
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

        protected UIMessageToken SubscribeMessage(string messageName, Action handler)
        {
            var token = UIManager.Instance.MessageCenter.Subscribe(messageName, handler, this);
            TrackMessageToken(token);
            return token;
        }

        protected UIMessageToken SubscribeMessage<T>(string messageName, Action<T> handler)
        {
            var token = UIManager.Instance.MessageCenter.Subscribe(messageName, handler, this);
            TrackMessageToken(token);
            return token;
        }

        protected void PublishMessage(string messageName)
        {
            UIManager.Instance.MessageCenter.Publish(messageName);
        }

        protected void PublishMessage<T>(string messageName, T payload)
        {
            UIManager.Instance.MessageCenter.Publish(messageName, payload);
        }

        protected void TrackMessageToken(UIMessageToken token)
        {
            if (token == null || token.IsDisposed)
            {
                return;
            }

            _messageTokens.Add(token);
        }

        protected void UnsubscribeAllMessages()
        {
            for (var i = _messageTokens.Count - 1; i >= 0; i--)
            {
                _messageTokens[i].Dispose();
            }

            _messageTokens.Clear();
            UIManager.Instance.MessageCenter.UnsubscribeOwner(this);
        }

        /// <summary>
        /// 设置当前 Context 的 ViewModel。替换时会释放旧实例。
        /// </summary>
        protected void SetViewModel(IViewModel viewModel)
        {
            if (ReferenceEquals(_viewModel, viewModel))
            {
                return;
            }

            ClearViewModel();
            _viewModel = viewModel;
        }

        protected T GetViewModel<T>() where T : class, IViewModel
        {
            return _viewModel as T;
        }

        /// <summary>
        /// 追踪绑定 token，OnDestroy 自动释放。
        /// </summary>
        protected void TrackBinding(IDisposable bindingToken)
        {
            if (bindingToken == null)
            {
                return;
            }

            if (bindingToken is BindingToken token && token.IsDisposed)
            {
                return;
            }

            _bindingTokens.Add(bindingToken);
        }

        protected void ClearBindings()
        {
            for (var i = _bindingTokens.Count - 1; i >= 0; i--)
            {
                try
                {
                    _bindingTokens[i]?.Dispose();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }

            _bindingTokens.Clear();
        }

        protected void ClearViewModel(bool dispose = true)
        {
            if (_viewModel == null)
            {
                return;
            }

            var vm = _viewModel;
            _viewModel = null;

            if (dispose)
            {
                vm.Dispose();
            }
        }
    }
}
