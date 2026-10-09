# Commercial virtual lists (stage 10)

## Installation and licensing

The production backend is **SuperScrollView 2.5.3**, used under the project's owner's
purchased license. It is not open source, and project-use authorization does not grant
permission to redistribute its source. No commercial Demo, Editor script, texture, scene,
or prefab is included in this integration.

Original adapter, installer, example, and test sources live in
`Integrations\SuperScrollView~`, outside Unity's `Assets` compilation inputs. From a local
Git checkout with Unity 2022.3.62f2, install your own licensed copy:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File ".\Integrations\SuperScrollView~\Install.ps1" `
  -VendorSource "D:\unityProjs\SuperScrollView\Assets\Plugins\SuperScrollView"
```

The installer verifies `Scripts\Version 2.5.3.txt` and 14 runtime C# files. It copies the
runtime `Scripts` directory with original names/metas and adds our independent assembly
definition. It does not change any supplier implementation or the source project.
It also copies our Runtime, Examples, and Tests into
`Assets\YUIFramework.SuperScrollView.Local`.

Before copying, the installer adds these exact local-only `.git/info/exclude` entries;
it never edits `.gitignore`:

```text
/Assets/YUIFramework.SuperScrollView.Local/
/Assets/YUIFramework.SuperScrollView.Local.meta
```

Never force-add that directory, include it in a public source archive, or bypass the
supplier's license. Git exclusions are an accidental-commit guard, not a license or
packaging security boundary. A new clone/CI machine needs its own authorized installation;
there is no download step. Rerun the installer after editing the original adapter sources.

Assembly direction is `YUIFramework.SuperScrollView -> YUIFramework.Runtime +
SuperScrollView + UniTask + Unity.ugui`. The vendor assembly references only uGUI. The core
framework has **no vendor dependency or missing-assembly reference**. Without installation,
core messaging, UI lifecycle, resources, and list contracts still compile and run; only the
optional adapter/example/tests are absent. Do not separately import another copy of the
same vendor scripts into this project.

## Public API and support

| Surface | Contract |
|---|---|
| `UIListDataSource<T>` | Borrowed `ObservableCollection<T>`, stable nonempty ordinal string ID selector |
| `UIListSelection` | Independent single/multi selection by ID, removed IDs are deselected |
| `SuperScrollViewOptions` | Snapshotted layout, item size, spacing, overscan, fixed grid columns |
| `SuperScrollViewList<T>` | Actual native List/Grid backend and exclusive native item-pool owner |
| `CreateAsync` | Acquires and retains a prefab asset lease; direct constructor borrows a prefab |
| `BeginDisplay(context, token)` | Returns the disposable display binding; observes context suspension |
| `ScrollTo`, `CaptureAnchor` | Strict data-index positioning, nonnegative leading offset, stable-ID anchor |
| `RefreshItem`, `RefreshVisible`, `NotifySizeChanged` | Local/visible rebind and dynamic List size notification |
| `UIListItemBinding` | Generation + ID + token, `TryApply`, generation-safe `LoadSpriteAsync` |
| `SetSuspended` | Explicit cooperative list gate, also driven by context visibility |
| `CreatedItemCount`, `BoundItemCount`, `LastError`, `Error` | Instance/binding counts and observable binder failures, not memory-byte estimates |

| Layout/feature | Status and boundary |
|---|---|
| Vertical and horizontal List | Fixed or dynamically bound sizes; tested across 10,000 data rows |
| Grid | Vertical, fixed cell size, fixed column count; independent native Grid backend |
| Dynamic Grid | Rejected by option validation; dynamic List support does not imply dynamic Grid |
| Insert/remove/move/replace/reset/clear | Real collection notifications, no per-frame data scan |
| Infinite looping, staggered grid, pagination/tree | Not exposed or claimed by this adapter; supplier Demo names are not acceptance evidence |
| Nested drag | Dominant-axis routing fixed at begin; same-axis remains child-owned |

IDs must remain stable for the lifetime of a data record. Change record contents via
`Replace`, retaining its ID when it is the same entity. Add/replace/reset duplicate IDs
fail **before** committing the collection. Mutation inside a collection notification is
rejected. Post-commit observer failures are aggregated after notifying the other observers;
an exception from that phase does not mean the data mutation rolled back. Structural
changes rebuild the ID index in O(n) on the event, not each frame. Replace refreshes only
the affected visible item; structural changes refresh the bounded visible native window.

An anchor records the nearest leading item's ID, previous index, and viewport-relative
offset. Structural changes restore that ID, or clamp the old index if it was removed.
Grid preserves the scrolling-axis offset; insertion may legitimately change the column.
An empty list has no anchor. Suspension retains the anchor; a new display starts at row 0.
List sizes must be finite and at least one unit in each dimension. Change a bound item's
RectTransform, then call `NotifySizeChanged(index)`; offscreen items use native estimated
sizes until bound. No whole-dataset layout measurement is performed.

## Binding, cancellation, and ownership

All list/collection/selection operations and cancellation that touches Unity objects are
**Unity-main-thread-only**. Async continuations must return to that thread. One display
binding is allowed at a time. Keep long-lived lists with `TrackBinding(list)` and call
`TrackDisplayBinding(list.BeginDisplay(this))` on each show. Alternatively track both the
list and its display token in the display scope to destroy the native pool on each close.

Native temporary-pool reuse can occur without `OnDisable`: every bind explicitly ends the
previous generation before setting the new identity. Offscreen deactivation, data rebind,
clear, hide, suspension, and disposal invalidate prior work and cancel its token.
`LoadSpriteAsync` awaits `IUIResourceService`, then checks generation, stable ID, and Unity
fake-null before applying. A late lease is disposed even if the loader ignored cancellation.
The installed example uses this API with the caller's YooAsset-backed service. Custom async
binders must await their work or attach an explicit error handler and fence writes through
`TryApply`; retaining and writing a Transform directly cannot be made safe by a token alone.

The plugin uses its **own Instantiate-based item pool**, never UIManager's context pool.
Do not return its objects to `UIObjectPool` or pretend they own provider instance leases.
`CreateAsync` holds one prefab asset lease for the private template and all native instances.
Explicit list disposal ends binds, destroys native instances/template/root, then releases
the prefab lease. Unexpected external root destruction defers that lease release to the
next player-loop frame so Unity can finish destroying children; explicitly dispose first
for deterministic UI -> resources -> bootstrap shutdown. Sprite leases end with their bind.
The list never disposes the caller's data source, selection, collection, models, or resource
service. The Context owner must track/dispose those it actually owns.

Hide/pool ends display subscriptions and binds; a lifetime-owned list may retain its
bounded native pool and prefab lease until Context release. Low-memory idle-context eviction
releases that list through existing Context cleanup. Active contexts retain ownership.
Scope closure does not forcibly destroy active UI. Canceled close/reverse restores
`Opened` without discarding the valid display binding. Suspension unsubscribes no permanent
model owner, stops native scrolling and item work, then rebinds current data on resume.
Cleanup attempts all items before aggregating failures. Binder errors populate `LastError`
and call `Error` (or log an exception if no handler); a subsequent refresh can recover.

`UINestedScrollRect` and the original routed native subclasses coordinate one begin/drag/end
sequence for the selected receiver. Supplier internal layout rebase events cannot replace a
live touch pointer ID. Hide/suspend/disable sends end to both ScrollRect and its native
participant. Inherited CanvasGroup interaction/raycast denial cancels active dragging;
the adapter never writes CanvasGroup flags or bypasses the stage 4 input authority.

## Example and migration

After installation, register `SuperScrollViewSamplePage` with an ordinary project-owned
page prefab containing `UIView`. Create a project-owned RectTransform cell prefab with
`SuperScrollViewSampleCell`, wire its Text/Image/Button fields, and make it addressable by
the existing YooAsset bootstrap/resource setup. Open with
`SuperScrollViewSamplePage.Arguments` containing `Resources`, the `ItemPrefab` key and an
optional `SpriteLocation`. The code-first example creates 10,000 rows, renders stable-ID
multi-selection, loads sprite leases, and exposes `InsertFirst`, `RemoveFirst`,
`MoveFirstToLast`, and `ScrollToMiddle` for project controls. It copies no supplier assets.
Formatting and per-cell click closures in this illustrative sample are not a zero-GC claim.

`UIVirtualList` is now obsolete and delegates to the installed backend. Its old standalone
kernel is removed. Missing installation throws with the installer path instead of silently
running a second engine. The compatibility driver retains index-based semantics (not
stable selection); migrate to the typed data source for structural mutations. Nonzero
legacy start/end padding and end-aligned scrolling are explicitly unsupported: represent
padding with the parent viewport, and use the typed adapter's leading offset.

See [Testing.md](Testing.md) for real-plugin, no-vendor, native-count, allocation, and
retained source-copy evidence. Tests run in Editor/Mono, not Android/iOS/IL2CPP devices.
Allocation scopes exclude deferred uGUI rebuilds, formatting, subscriptions/CTS creation,
and async resource loading. There is no all-UI-zero-GC or measured 60 FPS claim. Native pools
retain their viewport/overscan high-water allocation; they do not instantiate the entire
dataset or provide a separate global item-memory-budget manager.
