using System;
using System.Collections.Generic;
using UnityEngine;

namespace YUIFramework
{
    public sealed class UIMessageCenter : IUIMessageBus
    {
        private readonly Dictionary<string, List<UIMessageSubscription>> _subscriptionsByMessage =
            new Dictionary<string, List<UIMessageSubscription>>(StringComparer.Ordinal);
        private readonly Dictionary<UIMessageToken, UIMessageSubscription> _subscriptionsByToken =
            new Dictionary<UIMessageToken, UIMessageSubscription>();

        public int ListenerCount
        {
            get
            {
                var count = 0;
                foreach (var pair in _subscriptionsByMessage)
                {
                    var listeners = pair.Value;
                    for (var i = 0; i < listeners.Count; i++)
                    {
                        if (!listeners[i].IsDisposed)
                        {
                            count++;
                        }
                    }
                }

                return count;
            }
        }

        public UIMessageToken Subscribe(string messageName, Action handler, object owner = null)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            return SubscribeInternal(messageName, handler, owner);
        }

        public UIMessageToken Subscribe<T>(string messageName, Action<T> handler, object owner = null)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            return SubscribeInternal(messageName, handler, owner);
        }

        public void Publish(string messageName)
        {
            var normalizedMessageName = ValidateMessageName(messageName);
            if (!_subscriptionsByMessage.TryGetValue(normalizedMessageName, out var listeners) || listeners.Count == 0)
            {
                return;
            }

            PruneDisposed(normalizedMessageName, listeners);
            var snapshot = listeners.ToArray();
            for (var i = 0; i < snapshot.Length; i++)
            {
                var subscription = snapshot[i];
                if (subscription == null || subscription.IsDisposed)
                {
                    continue;
                }

                try
                {
                    if (subscription.Handler is Action action)
                    {
                        action.Invoke();
                    }
                    else if (subscription.Handler is Action<object> objectHandler)
                    {
                        objectHandler.Invoke(null);
                    }
                    else
                    {
                        Debug.LogWarning(
                            $"[UIMessageCenter] Publish(\"{normalizedMessageName}\") 与监听签名不匹配: {subscription.Handler.GetType().Name}");
                    }
                }
                catch (Exception ex)
                {
                    var wrapped = new UIMessageException(
                        normalizedMessageName,
                        $"消息分发失败: {normalizedMessageName} -> {subscription.Handler.Method.DeclaringType?.Name}.{subscription.Handler.Method.Name}",
                        ex);
                    Debug.LogException(wrapped);
                }
            }

            PruneDisposed(normalizedMessageName, listeners);
        }

        public void Publish<T>(string messageName, T payload)
        {
            var normalizedMessageName = ValidateMessageName(messageName);
            if (!_subscriptionsByMessage.TryGetValue(normalizedMessageName, out var listeners) || listeners.Count == 0)
            {
                return;
            }

            PruneDisposed(normalizedMessageName, listeners);
            var snapshot = listeners.ToArray();
            for (var i = 0; i < snapshot.Length; i++)
            {
                var subscription = snapshot[i];
                if (subscription == null || subscription.IsDisposed)
                {
                    continue;
                }

                try
                {
                    if (subscription.Handler is Action<T> typedHandler)
                    {
                        typedHandler.Invoke(payload);
                    }
                    else if (subscription.Handler is Action<object> objectHandler)
                    {
                        objectHandler.Invoke(payload);
                    }
                    else
                    {
                        Debug.LogWarning(
                            $"[UIMessageCenter] Publish<{typeof(T).Name}>(\"{normalizedMessageName}\") 与监听签名不匹配: {subscription.Handler.GetType().Name}");
                    }
                }
                catch (Exception ex)
                {
                    var wrapped = new UIMessageException(
                        normalizedMessageName,
                        $"消息分发失败: {normalizedMessageName} -> {subscription.Handler.Method.DeclaringType?.Name}.{subscription.Handler.Method.Name}",
                        ex);
                    Debug.LogException(wrapped);
                }
            }

            PruneDisposed(normalizedMessageName, listeners);
        }

        public void Unsubscribe(UIMessageToken token)
        {
            token?.Dispose();
        }

        public void UnsubscribeOwner(object owner)
        {
            if (owner == null || _subscriptionsByToken.Count == 0)
            {
                return;
            }

            var tokens = new List<UIMessageToken>();
            foreach (var pair in _subscriptionsByToken)
            {
                if (ReferenceEquals(pair.Value.Owner, owner))
                {
                    tokens.Add(pair.Key);
                }
            }

            for (var i = 0; i < tokens.Count; i++)
            {
                tokens[i].Dispose();
            }
        }

        public void Clear()
        {
            var tokens = new List<UIMessageToken>(_subscriptionsByToken.Keys);
            for (var i = 0; i < tokens.Count; i++)
            {
                tokens[i].Dispose();
            }

            _subscriptionsByToken.Clear();
            _subscriptionsByMessage.Clear();
        }

        public int Count(string messageName)
        {
            var normalizedMessageName = ValidateMessageName(messageName);
            if (!_subscriptionsByMessage.TryGetValue(normalizedMessageName, out var listeners))
            {
                return 0;
            }

            PruneDisposed(normalizedMessageName, listeners);
            return listeners.Count;
        }

        private UIMessageToken SubscribeInternal(string messageName, Delegate handler, object owner)
        {
            var normalizedMessageName = ValidateMessageName(messageName);
            if (!_subscriptionsByMessage.TryGetValue(normalizedMessageName, out var listeners))
            {
                listeners = new List<UIMessageSubscription>();
                _subscriptionsByMessage[normalizedMessageName] = listeners;
            }

            UIMessageToken token = null;
            var subscription = new UIMessageSubscription(normalizedMessageName, handler, owner, OnSubscriptionDisposed);
            token = new UIMessageToken(normalizedMessageName, () => RemoveSubscription(token, subscription));

            listeners.Add(subscription);
            _subscriptionsByToken[token] = subscription;
            return token;
        }

        private void OnSubscriptionDisposed(UIMessageSubscription subscription)
        {
            if (subscription == null || !_subscriptionsByMessage.TryGetValue(subscription.MessageName, out var listeners))
            {
                return;
            }

            listeners.Remove(subscription);
            if (listeners.Count == 0)
            {
                _subscriptionsByMessage.Remove(subscription.MessageName);
            }
        }

        private void RemoveSubscription(UIMessageToken token, UIMessageSubscription subscription)
        {
            if (token != null)
            {
                _subscriptionsByToken.Remove(token);
            }

            subscription?.Dispose();
        }

        private void PruneDisposed(string messageName, List<UIMessageSubscription> listeners)
        {
            for (var i = listeners.Count - 1; i >= 0; i--)
            {
                var subscription = listeners[i];
                if (subscription == null || subscription.IsDisposed)
                {
                    listeners.RemoveAt(i);
                }
            }

            if (listeners.Count == 0)
            {
                _subscriptionsByMessage.Remove(messageName);
            }
        }

        private static string ValidateMessageName(string messageName)
        {
            if (string.IsNullOrWhiteSpace(messageName))
            {
                throw new ArgumentException("messageName 不能为空。", nameof(messageName));
            }

            return messageName.Trim();
        }
    }
}
