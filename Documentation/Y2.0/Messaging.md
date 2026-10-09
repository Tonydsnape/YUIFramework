# YUIFramework Y2.0 Typed Messaging (Stage 9)

Stage 9 keeps one instance-owned `UIMessageCenter` behind `IUIMessageBus`. Typed and legacy
calls use the same channels; there is no compatibility-side registry.

## Typed topics

Define topics once and publish through the injected service or a context helper:

```csharp
public static readonly UIMessageTopic<PlayerScoreChanged> ScoreChanged =
    new UIMessageTopic<PlayerScoreChanged>("player.score.changed");

using var token = ui.MessageBus.Subscribe(
    ScoreChanged,
    message => RenderScore(message.Score),
    priority: 10);

ui.MessageBus.Publish(ScoreChanged, new PlayerScoreChanged(score));
```

While a topic name has a registered channel, it has exactly one payload type. Subscribing or
publishing that name with another type throws `UIMessageException` immediately. The string
overloads are `[Obsolete]` facades that construct a typed topic and forward to this same
rule.

## Ordering, mutation, and errors

- Higher priority runs first.
- Equal priority runs in subscription order.
- Subscribe/unsubscribe rebuilds an immutable channel snapshot. A normal publish allocates
  no snapshot or temporary list.
- A subscription added during publish is absent from that publish but visible to a recursive
  publish. A token disposed before its turn is skipped immediately, including in the current
  snapshot.
- Recursive publish is supported and uses the channel snapshot current at recursion time.
- `Clear` disposes all tokens; a publish already walking an old snapshot observes the disposed
  flags and skips them.
- One subscriber exception does not block later subscribers. Failures are wrapped as
  `UIMessageException` and thrown to the publisher as one `AggregateException` after dispatch.

The bus is Unity-main-thread confined. Construction fixes the owning thread, and subscribe,
publish, count, clear, and direct token disposal reject another thread. A cancellation that
ends a scope on a worker thread posts its cleanup to the Unity synchronization context
captured by the bus. If no owning context exists, off-thread scope disposal fails without
invalidating its tokens so cleanup can be retried on the owning thread. Payload objects are
not cloned.

## Scope and context ownership

`CreateScope(name, cancellationToken)` returns an idempotent `UIMessageScope`. Subscriptions
attached to it are disposed when the scope or token ends; attaching to an ended scope fails.

`BaseContext` provides:

- `SubscribeMessage(topic, handler)` for context-lifetime subscriptions.
- `SubscribeDisplayMessage(topic, handler)` for the current `DisplayToken` cycle.
- `PublishMessage(topic, payload)` for the owning injected bus.

Display subscriptions are removed on successful hide/pool, failed-open rollback, terminal
release, and shutdown. Ending a scene/module scope immediately evicts its idle entries and
prevents later return to that scope, but stage 7 deliberately does not destroy an already
active UI. Its display subscriptions therefore remain until that active context is
subsequently closed or terminally released. A reversed/canceled close that rolls back to
`Opened` likewise retains the still-active display scope. Lifetime subscriptions remain
while pooled and are removed at `OnDestroy`. Shutdown clears the bus after active and pooled
contexts have run their cleanup, so the center never becomes a static strong-reference root.

## Allocation boundary

The typed channel invokes `Action<T>` directly, so value payloads are not boxed. Subscription
mutation allocates a replacement snapshot by design; steady-state publish does not. Stage 9
measures the pure typed-publish loop separately from formatting strings and Unity control
internals. This is an Editor/Mono managed-allocation baseline, not a device frame-rate claim.
