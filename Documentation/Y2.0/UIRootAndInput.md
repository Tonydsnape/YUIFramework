# UIRoot, Layers, Sorting, and Input (Stage 4)

Stage 4 makes the uGUI composition boundary explicit. `UIRootRuntime` owns the
relationship between a complete `UIRoot`, one `EventSystem`, the ten-layer profile,
sorting leases, modal interaction, input locks, focus, and back input.

## Root and EventSystem ownership

Use one of two construction paths:

```csharp
var runtime = UIRootRuntime.CreateOwned();
ui.Initialize(loader, runtime);
```

```csharp
var runtime = UIRootRuntime.CreateExternal(root, eventSystem, options);
ui.Initialize(loader, runtime);
```

The owned path creates the root with `RectTransform`, `Canvas`, `CanvasScaler`, and
`GraphicRaycaster` before adding `UIRoot`, then creates `EventSystem` and
`StandaloneInputModule`. Shutdown destroys both owned objects. The external path validates
the complete objects and never destroys them; generated layer children are runtime-owned
and removed. A non-overlay render mode requires an explicit camera. Duplicate claimed roots
or a different current EventSystem fail deterministically.

`UIRoot` never calls `FindObjectOfType` and never creates an empty object. Its obsolete
`Instance` property returns the explicitly active root or throws. Static root state resets
at `SubsystemRegistration`, release, and destruction. If an external scene-owned root is
destroyed while active, subsequent layer access throws `UIRootUnavailableException`.

## Ten-layer profile

`UILayerProfile` is the only source of ordering and policy:

| Order | Layer | Default sorting base | Modal |
|---:|---|---:|---|
| 0 | Background | 0 | no |
| 1 | Scene | 1000 | no |
| 2 | Normal | 2000 | no |
| 3 | Fixed | 3000 | no |
| 4 | Popup | 4000 | yes |
| 5 | Guide | 5000 | no |
| 6 | Toast | 6000 | no |
| 7 | Loading | 7000 | yes |
| 8 | System | 8000 | yes |
| 9 | Debug | 9000 | no |

Profiles must contain each canonical layer exactly once in this order. Capacity and sorting
ranges are validated for overlap and Unity's signed 16-bit Canvas sorting range.
`Bottom` maps to `Background`; `Top` maps to `Toast`. Legacy enum numeric values for
`Scene`, `Normal`, and `System` remain unchanged, but runtime ordering always comes from the
profile and never from `Enum.GetValues`.

Each layer root is a stretched `RectTransform` with its own sorting `Canvas` and
`GraphicRaycaster`.

## Sorting leases

Every bound context receives one `UISortingLease`. Sorting is compact and bounded inside
the layer's configured capacity; view orders use even slots and the odd slot immediately
below a modal is reserved for the shared mask. `BringToTop` moves the existing lease and
reindexes active leases instead of increasing a cursor.

Hidden navigation entries retain their lease and relative position. Pooling, release,
terminal failure, and shutdown dispose the lease. Disposal is idempotent. Capacity
exhaustion throws instead of wrapping or crossing into another layer. Failed refreshes
restore the previous lease position.

## Modal interaction

`UIInteractionController` is the sole writer of layer and view raycast eligibility. It
combines:

```text
visible && profile-interactable && modal-eligible && input-lock-eligible
```

`UIModalService` keeps a context-identity stack and one reusable mask. Only the top modal
and eligible higher layers can receive raycasts; lower modals and lower layers are blocked.
Eligibility is applied to every descendant `GraphicRaycaster`, and focus is cleared or
moved so Submit/Move cannot bypass a pointer lock.
The mask is parented to the top modal's layer and sorted in the reserved slot immediately
below it. Hide, pool, release, failed/cancelled open, and shutdown remove modal state.

`UIConfig.UseLayerModalPolicy` defaults to true. Set it to false and use `UIConfig.Modal`
only for an explicit per-context override.

## Reference-counted input locks

`UIInputLockService.Acquire(owner, reason, allowedLayers)` returns an idempotent
`IDisposable` lease. Locks have independent IDs and expose owner/reason diagnostics.
Multiple whitelist sets are intersected, so nested locks always use the most restrictive
result. Releasing leases out of order is safe; the last release restores profile/modal
interaction. Shutdown records `UIManager.LastShutdownInputLockLeakCount` and forcibly
clears remaining leases.

## Back input and focus

`UIInputRouter` uses an `IUIBackInputSource`; the default legacy source maps Unity
`KeyCode.Escape` to desktop Escape and Android Back without depending on the new Input
System. Pointer and touch dispatch remain with uGUI `EventSystem`. Requests route only to
`IUINavigator.NavigateBackAsync`, are rejected while navigation is busy, input is locked,
or another back request is in flight, and clear their in-flight state in `finally`.
The early input driver suppresses EventSystem navigation for that frame, preventing the
legacy `Cancel` mapping from dispatching a second Escape action while pointer/touch
processing remains enabled.

On show, `UIFocusService` saves the current selection by context identity and selects
`BaseContext.DefaultFocus`, falling back to the first active interactable `Selectable`.
Hide/close/back restores a still-valid prior object. Destroyed or inactive selections fall
back only to the current top active context, otherwise focus is cleared. Modal changes
always move focus to the top modal.

## Shutdown boundary

Stage 3 ordering is preserved: stop new navigation work, cancel service lifetime, drain the
navigator, stop and drain per-type lanes, then close active contexts and clear pools.
Only after those steps does stage 4 clear interaction, focus, input locks, layers, and the
root runtime. Concurrent shutdown callers share one completion task and cannot interleave
cleanup. Stage 5 resource leases are not implemented here.
