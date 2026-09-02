# YUIFramework Y2.0 Baseline

## Verified environment

| Item | Baseline |
|---|---|
| Unity | 2022.3.62f2 |
| uGUI | 1.0.0 |
| TextMeshPro | 3.0.7 |
| YooAsset | 3.0.5 |
| UniTask | Git package, locked at `ceac8d6946b1125fe782cd171fbcb245b567dbf9` |
| Primary targets | Android and iOS |
| Development host | Windows Editor |

## Current assembly boundaries

```text
YUIFramework.Runtime
YUIFramework.HotUpdate -> YUIFramework.Runtime, YooAsset, UniTask
YUIFramework.Editor -> YUIFramework.Runtime, YUIFramework.HotUpdate
Examples -> Assembly-CSharp
```

`YUIFramework.HotUpdate` currently means YooAsset resource update/bootstrap code. Y2.0 will rename and split this boundary before HybridCLR integration so resource bootstrap cannot be confused with code hot update.

## Frozen Y1 behavior

The characterization tests intentionally preserve these current behaviors:

- Context registration is keyed by context type.
- Reopening an active context returns the same instance and calls `OnShow` again.
- A non-cached close calls `OnHide`, `OnClose`, `OnDestroy`, then resource release.
- A cached close calls `OnHide` and `OnClose`, stores the context, and defers destruction.
- Restoring a pooled context does not call `OnInit` again.
- Push hides the previous page by default.
- Back closes the top page and shows the previous page.
- Replace closes the current page by default.
- Bringing an existing page to the top closes pages above it.

These tests describe existing behavior; they do not certify concurrency safety or production readiness.

## Known risks intentionally not fixed in phase 0

- UI open/close operations are not serialized.
- Navigation does not use transactions or rollback.
- New, active, and pooled open paths use inconsistent lifecycle states.
- Transitions cannot be canceled and can race with close/reopen.
- Resource ownership semantics vary by loader.
- Runtime singletons do not provide complete shutdown/reset hooks.
- `UIRoot.Instance` creates a plain GameObject and adds `UIRoot` before required Canvas components are ready; in PlayMode this can throw `MissingComponentException`. Baseline tests provision a complete root explicitly, and the runtime fix is deferred to the UIRoot phase.
- Pool subscriptions, bindings, and asynchronous operations are not automatically scoped.
- Sorting order only increases.
- There is no production diagnostics surface.

Fixing these items belongs to later Y2.0 phases and must be protected by this baseline.

## Workspace protection

At phase 0 start, `Assets/YUIFramework/Runtime/Core/UIManager.cs` contained a pre-existing uncommitted whitespace-only change after `UIManager.Instance`. It is user-owned and must not be overwritten or reverted by baseline work.
