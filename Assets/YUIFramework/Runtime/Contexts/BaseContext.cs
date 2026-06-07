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
    }
}
