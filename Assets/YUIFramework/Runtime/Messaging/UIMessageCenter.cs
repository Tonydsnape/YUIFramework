using System;
using System.Collections.Generic;
using System.Threading;

namespace YUIFramework
{
    public sealed class UIMessageCenter : IUIMessageBus
    {
        private readonly Dictionary<string, UIMessageChannel> _channels =
            new Dictionary<string, UIMessageChannel>(StringComparer.Ordinal);
        private readonly Dictionary<UIMessageToken, UIMessageSubscription> _subscriptions =
            new Dictionary<UIMessageToken, UIMessageSubscription>();
        private readonly int _owningThreadId = Environment.CurrentManagedThreadId;
        private readonly SynchronizationContext _owningSynchronizationContext =
            SynchronizationContext.Current;
        private long _nextSequence;

        public int ListenerCount => _subscriptions.Count;

        public UIMessageScope CreateScope(
            string name = null,
            CancellationToken cancellationToken = default)
        {
            EnsureOwningThread();
            cancellationToken.ThrowIfCancellationRequested();
            return new UIMessageScope(
                name,
                cancellationToken,
                _owningThreadId,
                _owningSynchronizationContext);
        }

        public UIMessageToken Subscribe<T>(
            UIMessageTopic<T> topic,
            Action<T> handler,
            UIMessageScope scope = null,
            int priority = 0,
            object owner = null)
        {
            EnsureOwningThread();
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            var name = ValidateMessageName(topic.Name);
            var channel = GetOrCreateChannel<T>(name);
            var subscription = new UIMessageSubscription<T>(
                name,
                handler,
                owner,
                priority,
                ++_nextSequence,
                OnSubscriptionDisposed);
            UIMessageToken token = null;
            token = new UIMessageToken(
                name,
                typeof(T),
                priority,
                () => RemoveSubscription(token, subscription));
            channel.Add(subscription);
            _subscriptions[token] = subscription;
            try
            {
                scope?.Attach(token);
                return token;
            }
            catch
            {
                token.Dispose();
                throw;
            }
        }

        public void Publish<T>(UIMessageTopic<T> topic, T payload)
        {
            EnsureOwningThread();
            var name = ValidateMessageName(topic.Name);
            if (!_channels.TryGetValue(name, out var channel))
            {
                return;
            }

            if (!(channel is UIMessageChannel<T> typedChannel))
            {
                throw CreatePayloadMismatch(name, typeof(T), channel.PayloadType);
            }

            typedChannel.Publish(payload);
        }

        public int Count<T>(UIMessageTopic<T> topic)
        {
            EnsureOwningThread();
            var name = ValidateMessageName(topic.Name);
            if (!_channels.TryGetValue(name, out var channel))
            {
                return 0;
            }

            if (!(channel is UIMessageChannel<T>))
            {
                throw CreatePayloadMismatch(name, typeof(T), channel.PayloadType);
            }

            return channel.Count;
        }

        [Obsolete("Use Subscribe(UIMessageTopic<UIMessageUnit>, ...). String message APIs will be removed after the Y2 migration window.")]
        public UIMessageToken Subscribe(string messageName, Action handler, object owner = null)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            return Subscribe(
                new UIMessageTopic<UIMessageUnit>(messageName),
                _ => handler(),
                owner: owner);
        }

        [Obsolete("Use Subscribe(UIMessageTopic<T>, ...). String message APIs will be removed after the Y2 migration window.")]
        public UIMessageToken Subscribe<T>(
            string messageName,
            Action<T> handler,
            object owner = null)
        {
            return Subscribe(
                new UIMessageTopic<T>(messageName),
                handler,
                owner: owner);
        }

        [Obsolete("Use Publish(UIMessageTopic<UIMessageUnit>, UIMessageUnit.Value). String message APIs will be removed after the Y2 migration window.")]
        public void Publish(string messageName)
        {
            Publish(
                new UIMessageTopic<UIMessageUnit>(messageName),
                UIMessageUnit.Value);
        }

        [Obsolete("Use Publish(UIMessageTopic<T>, payload). String message APIs will be removed after the Y2 migration window.")]
        public void Publish<T>(string messageName, T payload)
        {
            Publish(new UIMessageTopic<T>(messageName), payload);
        }

        public void Unsubscribe(UIMessageToken token)
        {
            EnsureOwningThread();
            token?.Dispose();
        }

        public void UnsubscribeOwner(object owner)
        {
            EnsureOwningThread();
            if (owner == null || _subscriptions.Count == 0)
            {
                return;
            }

            var tokens = new List<UIMessageToken>();
            foreach (var pair in _subscriptions)
            {
                if (ReferenceEquals(pair.Value.Owner, owner))
                {
                    tokens.Add(pair.Key);
                }
            }

            DisposeTokens(tokens);
        }

        public void Clear()
        {
            EnsureOwningThread();
            var tokens = new List<UIMessageToken>(_subscriptions.Keys);
            DisposeTokens(tokens);
            _subscriptions.Clear();
            _channels.Clear();
        }

        [Obsolete("Use Count(UIMessageTopic<T>). String message APIs will be removed after the Y2 migration window.")]
        public int Count(string messageName)
        {
            EnsureOwningThread();
            var name = ValidateMessageName(messageName);
            return _channels.TryGetValue(name, out var channel)
                ? channel.Count
                : 0;
        }

        private UIMessageChannel<T> GetOrCreateChannel<T>(string name)
        {
            if (_channels.TryGetValue(name, out var existing))
            {
                if (existing is UIMessageChannel<T> typed)
                {
                    return typed;
                }

                throw CreatePayloadMismatch(name, typeof(T), existing.PayloadType);
            }

            var channel = new UIMessageChannel<T>(name);
            _channels.Add(name, channel);
            return channel;
        }

        private void OnSubscriptionDisposed(UIMessageSubscription subscription)
        {
            if (subscription == null ||
                !_channels.TryGetValue(subscription.MessageName, out var channel))
            {
                return;
            }

            channel.Remove(subscription);
            if (channel.Count == 0)
            {
                _channels.Remove(subscription.MessageName);
            }
        }

        private void RemoveSubscription(
            UIMessageToken token,
            UIMessageSubscription subscription)
        {
            EnsureOwningThread();
            if (token != null)
            {
                _subscriptions.Remove(token);
            }

            subscription?.Dispose();
        }

        private static void DisposeTokens(List<UIMessageToken> tokens)
        {
            List<Exception> errors = null;
            for (var index = tokens.Count - 1; index >= 0; index--)
            {
                try
                {
                    tokens[index]?.Dispose();
                }
                catch (Exception exception)
                {
                    errors ??= new List<Exception>();
                    errors.Add(exception);
                }
            }

            if (errors != null)
            {
                throw new AggregateException("Message subscription cleanup failed.", errors);
            }
        }

        private void EnsureOwningThread()
        {
            if (Environment.CurrentManagedThreadId != _owningThreadId)
            {
                throw new InvalidOperationException(
                    "UIMessageCenter may only be used from its owning Unity thread.");
            }
        }

        private static UIMessageException CreatePayloadMismatch(
            string name,
            Type requested,
            Type registered)
        {
            return new UIMessageException(
                name,
                $"Message topic '{name}' is registered for {registered.FullName}, not {requested.FullName}.");
        }

        private static string ValidateMessageName(string messageName)
        {
            if (string.IsNullOrWhiteSpace(messageName))
            {
                throw new ArgumentException("A message topic name is required.", nameof(messageName));
            }

            return messageName.Trim();
        }

        private abstract class UIMessageChannel
        {
            protected UIMessageChannel(string name, Type payloadType)
            {
                Name = name;
                PayloadType = payloadType;
            }

            protected string Name { get; }
            public Type PayloadType { get; }
            public abstract int Count { get; }
            public abstract void Add(UIMessageSubscription subscription);
            public abstract void Remove(UIMessageSubscription subscription);
        }

        private sealed class UIMessageChannel<T> : UIMessageChannel
        {
            private readonly List<UIMessageSubscription<T>> _subscriptions =
                new List<UIMessageSubscription<T>>();
            private UIMessageSubscription<T>[] _snapshot =
                Array.Empty<UIMessageSubscription<T>>();

            public UIMessageChannel(string name)
                : base(name, typeof(T))
            {
            }

            public override int Count => _subscriptions.Count;

            public override void Add(UIMessageSubscription subscription)
            {
                var typed = (UIMessageSubscription<T>)subscription;
                var index = _subscriptions.Count;
                for (var current = 0; current < _subscriptions.Count; current++)
                {
                    var existing = _subscriptions[current];
                    if (typed.Priority > existing.Priority ||
                        typed.Priority == existing.Priority &&
                        typed.Sequence < existing.Sequence)
                    {
                        index = current;
                        break;
                    }
                }

                _subscriptions.Insert(index, typed);
                _snapshot = _subscriptions.ToArray();
            }

            public override void Remove(UIMessageSubscription subscription)
            {
                if (_subscriptions.Remove((UIMessageSubscription<T>)subscription))
                {
                    _snapshot = _subscriptions.Count == 0
                        ? Array.Empty<UIMessageSubscription<T>>()
                        : _subscriptions.ToArray();
                }
            }

            public void Publish(T payload)
            {
                var snapshot = _snapshot;
                List<Exception> errors = null;
                for (var index = 0; index < snapshot.Length; index++)
                {
                    var subscription = snapshot[index];
                    if (subscription.IsDisposed)
                    {
                        continue;
                    }

                    try
                    {
                        subscription.Invoke(payload);
                    }
                    catch (Exception exception)
                    {
                        errors ??= new List<Exception>();
                        errors.Add(
                            new UIMessageException(
                                Name,
                                $"Message dispatch failed for {subscription.Handler.Method.DeclaringType?.Name}.{subscription.Handler.Method.Name}.",
                                exception));
                    }
                }

                if (errors != null)
                {
                    throw new AggregateException(
                        $"One or more subscribers failed while publishing '{Name}'.",
                        errors);
                }
            }
        }
    }
}
