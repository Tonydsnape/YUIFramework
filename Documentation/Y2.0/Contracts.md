# Y2 Runtime Contracts

## Service ownership

Y2 code owns an `IUIService` instance and injects it at the application composition root:

```csharp
IUIService ui = new UIManager();
var rootRuntime = UIRootRuntime.CreateOwned();
await ui.InitializeAsync(resourceLoader, rootRuntime, cancellationToken: cancellationToken);
```

`UIManager.Instance` and `UIManager.Init` remain available only as a temporary Y1 compatibility entry. They use the same `UIManager` implementation; there is no second registry, navigator, pool, or message bus.

The owner must call:

```csharp
await ui.ShutdownAsync();
```

Shutdown stops both the per-type operation coordinator and the navigator's FIFO queue from accepting new commands, cancels the service lifetime so in-flight and still-queued operations observe cancellation, drains every lane (queued and running) before touching any state, closes active contexts, destroys pooled contexts, clears navigation/messages/registration, then disposes the root runtime. The injected resource loader is caller-owned; Y2 does not dispose it. External root/EventSystem objects remain caller-owned. See [Navigation.md](Navigation.md) and [UIRootAndInput.md](UIRootAndInput.md).

## Initialization rules

- `Initialize` and `InitializeAsync` may only be called while uninitialized.
- Repeated initialization throws `InvalidOperationException`.
- A canceled `InitializeAsync` throws before changing service state.
- `ShutdownAsync` is idempotent while uninitialized.
- Cancellation is checked before shutdown starts. Once cleanup starts, shutdown completes cleanup without partial cancellation.
- The same service instance may be initialized again after a successful shutdown.
- Initialization accepts an explicit `UIRootRuntime`; the compatibility overload creates or binds one through the same implementation.
- Public service operations, including generic `CloseAsync<T>`, reject use before initialization and after shutdown with `InvalidOperationException`.

## Async baseline

Runtime, navigation, resource, and transition asynchronous contracts use:

- `UniTask` / `UniTask<T>`
- an optional `CancellationToken`
- cancellation propagation through `OperationCanceledException`

Each context operation links the caller, context lifetime, and service lifetime cancellation tokens. A canceled new open releases its instance and resource; a canceled pooled open returns the context to the pool; a canceled existing open restores its prior stable state; and a canceled close keeps the context reachable and restores transition visuals.

Lifecycle callback failures record `LastFailure` and surface as metadata-rich `UILifecycleException`. Rollback failures force release rather than retaining a damaged active or pooled context. See [Lifecycle.md](Lifecycle.md) for the complete state graph and callback phases.

## Registry

`IUIRegistry` provides:

- `Register<T>(UIConfig)`
- `IsRegistered<T>()`
- `TryGetConfig(Type, out UIConfig)`

`UIConfig.Id` is exposed as an ordinal, case-sensitive `UIKey`. Empty keys are rejected.

Stage 4 adds validated `UILayerProfile`/`UILayerDescriptor` runtime descriptors. The mutable `UIConfig` registration adapter remains during the migration window.

## Handles

`OpenHandleAsync<T>` returns `UIHandle<T>`:

- `Key` identifies the registered UI.
- `Context` exposes the current context during the migration window.
- `IsOpen` checks that the handle still refers to the active context.
- `CloseAsync` routes closing through the service that created the handle.
- Closing a stale handle is a no-op and cannot remove a newer context of the same type.

Reference identity prevents a stale handle from closing a newer active context of the same type.

For YooAsset loads, cancellation stops the caller's wait immediately. If YooAsset cannot abort the underlying provider operation, the framework retains a background release continuation and releases the handle as soon as that operation completes.

## Context dependency scope

`BaseContext` receives its owning `IUIService` when bound. Protected message helpers now use the owning service's `IUIMessageBus`; they no longer route through `UIManager.Instance`.

Derived contexts can access the owning service through the protected `Services` property. This is the supported route for navigation and service interaction in injected Y2 applications.

## Navigation

`IUINavigator` exposes the current navigation API using UniTask and cancellation.

Stage 3 gives every UI operation FIFO ordering per context type, gives the navigator its
own single FIFO transaction queue (`IsBusy` reports its activity), adds a public
`BringToTopAsync<T>`, a `NavigateBackAsync` alias for `BackAsync`, an async navigation
guard extension point, and non-destructive-first transaction rollback with
destructive-failure convergence for Push/Pop/Replace/BringToTop. Per-context lifecycle
re-entry still fails immediately with `UIOperationInProgressException` as a last-resort
safety net; the callback/guard-scoped reentrancy check
(`UIOperationReentrancyException`) is now the first line of defense and fires before a
callback could ever deadlock by awaiting its own in-progress command. See
[Navigation.md](Navigation.md) for the full merge policy, ordering guarantees, guard
contract, and documented limitations.

## Root, interaction, and input

`IUIService.RootRuntime` exposes the explicit uGUI composition boundary and
`IUIService.InputLocks` exposes reference-counted input leases. Ten canonical layers,
bounded sorting leases, one modal mask/stack, focus restoration, and Escape/Android Back
routing are defined in [UIRootAndInput.md](UIRootAndInput.md).

## Compatibility window

The following remain Y1 compatibility APIs:

- `UIManager.Instance`
- `UIManager.Init`
- object-based open arguments
- direct `BaseContext` exposure
- mutable `UIConfig`
- string-based messages

New code should start from `Y2ServiceBootstrap` and use an owned `IUIService`.
