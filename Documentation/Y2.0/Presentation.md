# Stage 11: screen adaptation, presentation and accessibility

This is an optional, instance-owned layer over the existing Config, text,
resources, Context and transition services. There is no second configuration
store, static presentation singleton, Addressables or Unity Localization package.
Stage 12 scene services and stage 14 build tooling are not part of this work.

## Run and maintain the example

Use the existing `HelloUIBootstrap` in a scene. Its original Config startup now
composes `TextLocalizationService` and `UIPresentationService`, then opens
`SampleHelloPage` with `SampleLocalizedHelloArgs`. It displays real TMP Chinese,
English and French, a locale badge, date/currency, theme/font-size/contrast/motion
controls and keyboard focus feedback. The list scrolls inside its safe viewport;
keyboard focus scrolls its selected control into view. Returning with null
navigation args retains the borrowed presentation owner. Plain-string and
text-only arguments remain compatible. This does **not** theme every old example.

`HotUpdateStartupSample` follows the same path **after verified YooAsset resources**:
Bootstrap ready -> Config atomic initialize -> explicit UI batch -> sample entry.
Config failure prevents entry. The six UISettings rows remain unchanged.

Edit `Config\LocalizationTextConfig.xlsx` (nine keys, en/zh-CN/fr) and
`Config\UIPresentationConfig.xlsx` (sixteen rows). From `Config`:

```powershell
npm.cmd test
npm.cmd run validate
npm.cmd run export:client
```

The standard transactional exporter publishes three tables together and rolls
back failed managed output swaps. Invalid input leaves all prior generated files
intact. `tools\create-presentation-workbook.js` creates the missing sample workbook,
not an overwrite of maintained data. The localization extension script adds only
the five missing stage-11 control keys. Config remains independent of Unity UI
and the Localization assembly.

## Typed tokens and lifecycle

`UIPresentationConfig` is a required normal table with composite `Group/Key`.
Columns are Group, Key, Kind, Value, optional HighContrast and Package.
Groups `theme.light` and `theme.dark` have the same five tokens:
`text`/`panel` (Color), `body` (Font), `badge` (Sprite), `surface` (Material).
`locale.en`, `locale.zh-CN`, `locale.fr` optionally override **Font/Sprite only**.
Theme values are the documented default when no locale asset override exists;
this is not a resource-load fallback. Missing/wrong-type tokens, invalid colors,
inconsistent theme keys or invalid locale groups fail explicitly.
Node validation and `UIPresentationCatalog.Parse` enforce the schema before
publication. Entries and typed snapshots are immutable; successful reloads refresh
the existing view without changing UI registration identity.

```csharp
var presentation = new UIPresentationService(configs, text, resources,
    ui.Transitions, theme: "light", defaultPackage: packageName);
presentation.SetTheme("dark");
presentation.SetAccessibility(new UIAccessibilityOptions(
    fontScale: 1.5f, highContrast: true, reducedMotion: true));
Color color = presentation.Color(new UIThemeToken<Color>("text"));
using var sprite = await presentation.LoadAsync(
    new UIThemeToken<Sprite>("badge"), token);

// Graphic/TMP/Image component, injected from Context.HandleShow:
style.SetTokens(color: "text", font: "body");
TrackDisplayBinding(style.Bind(presentation, this));
await style.LastRefresh;
```

`ConfigPresentationBinding` caches controls, captures their original visual
state, loads all requested assets before applying, and swaps lease bags only
after a successful current-generation update. Version + service identity +
token + Unity fake-null guards prevent late results from overwriting a replacement.
Each stale/failed partially acquired bag is released, including noncooperating
provider completions handled by the shared resource service. It never enters
the UI instance pool or disposes borrowed services.

Disable/display end restores original font/material/sprite/color/sizing before
releasing assets. Re-enable refreshes, rebind replaces the old owner, and an old
binding handle cannot unbind its replacement. Font scale is relative to a saved
baseline, never cumulative. Active suspension cancels pending work and defers
updates; resume applies current settings. Actual Hide/Pool/Close/failed-show and
Shutdown end display bindings; a canceled close restored to Opened retains them.
Binding before Config is ready is supported. Config shutdown restores baselines;
reinitialize/reload refreshes without another registry.

`RefreshAsync(token)` exposes caller cancellation/failure. Event refreshes also
log failures and expose an independently awaitable `LastRefresh` and `LastFailure`.
Failures leave the last complete visual set intact. Cleanup continues across
individual lease failures and aggregates with the primary load error.
Service events dispatch a captured subscriber list in order and aggregate observer
failures after all observers run; mutations during dispatch are rejected.
All lookup/mutation/Unity control work is on the owning Unity thread.
Teardown order: **UI -> presentation -> text -> Config -> resources -> Bootstrap**.

## Font provenance, atlas and production addresses

The bundled baseline is **LXGW WenKai Regular v1.522**, a modern Kai-style reading
face based on Klee One, not traditional handwritten calligraphy. The unchanged
official font is `Assets\YUIFramework\ThirdParty\LXGWWenKai\LXGWWenKai-Regular.ttf`.

- Release: <https://github.com/lxgw/LxgwWenKai/releases/tag/v1.522>
- Download: <https://github.com/lxgw/LxgwWenKai/releases/download/v1.522/LXGWWenKai-Regular.ttf>
- SHA256: `39ad71264b588165b469e35e6afb162a378dacd1f95348160240ba9038ac3009`
- Size: 25,575,676 bytes; one weight only.
- Original OFL 1.1 and copyright are retained beside the font, with `Source.json`.
  Commercial embedding/bundling/redistribution is permitted under OFL; standalone
  font sale is not. Font/TMP font derivatives remain OFL, not the framework's license.

`Assets\Resources\YUIPresentation\WenKai.asset` is a real persisted static TMP
font with a 2048x2048 Alpha8 atlas (about 4 MiB texture payload). It prewarms the
sample strings/ASCII at 48pt with 5px SDF padding. Its one dynamic
`WenKaiFallback.asset` retains the source TTF, with **multi-atlas disabled**:
at most one further 2048x2048 Alpha8 atlas. Filling it cannot silently allocate
an unbounded series of atlases; unsupported/full glyph requests remain explicit
missing-glyph diagnostics. TTF CPU/font-engine memory and materials are additional,
not included in the 8 MiB combined atlas ceiling.

TMP settings reference this base font, making it an intentional application-
resident baseline, independent of per-display override leases. Releasing all
presentation leases does not claim to unload this globally referenced base font.
The minimal sample explicitly uses Resources; production presentation requests use
the verified YooAsset service and explicit Package column/default package.
Collect these addresses, plus their dependency closure:

| Address | Asset |
|---|---|
| `YUIPresentation/WenKai` | TMP primary font; include fallback, atlases, materials, shader and original TTF |
| `YUIPresentation/BadgeEn`, `BadgeZh`, `BadgeFr` (each with the `YUIPresentation/` prefix) | Original 32x32 sample sprites |
| `YUIPresentation/Panel` | Original UI/Default material |
| `UISettings`, `LocalizationTextConfig`, `UIPresentationConfig` | MessagePack TextAssets from `Assets\Resources\YUIConfig` |

Editor Config reads the existing EditorJson outputs. The same presentation asset
addresses apply in both modes; no missing YooAsset address silently routes to
Resources. Replace/move the sample Resources assets and TMP defaults deliberately
for a production packaging policy; a real CDN/player build is not claimed here.

The five TMP shader/include files and two punctuation line-breaking tables are
unchanged TMP 3.0.7 package resources, with original GUIDs and the Unity Companion
License preserved at `ThirdParty\TMPShaders\LICENSE.md`. They are for Unity projects,
not OFL assets. `YUIFramework > Stage 11 > Create missing sample assets` creates
missing sample assets and fills missing settings references; it does not replace
existing user fonts or rebuild an existing static atlas automatically.
After maintaining translations, the explicit **Warm sample font from exported text**
command adds their glyphs plus currency/TMP punctuation to the existing static
atlas without replacing its GUIDs. Atlas-full/missing-source glyphs are errors.

## Scaling, safe-area hierarchy and Editor preview

`UIRootRuntimeOptions.ScalePolicy` supplies a validated `UIScalePolicy`:
reference resolution, CanvasScaler width/height match (0..1), Match/Expand/Shrink.
The default preserves the previous 1920x1080, match-width policy. External-root
scaler settings are restored on release.

One `UIScreenLayout` observes screen size/safe area and canvas viewport per root.
Unchanged observations do not notify. `IUIScreenSource`/`Apply(frame, viewport)`
allow deterministic device tests without changing Screen resolution.
`UISafeAreaContent.Bind(root.ScreenLayout)` only insets an explicitly selected
**content** RectTransform and restores its original anchors/offsets on disable/
unbind. Layer roots, fullscreen backgrounds and modal blockers remain full-canvas;
the notch must not become an input hole.
Conversion accounts for canvas viewport, nested offsets/scales and intersects the
parent bounds. Content axes must align with the canvas; rotated safe parents are
explicitly rejected rather than given incorrect geometry.

`YUIFramework > Stage 11 > Device and safe-area preview` owns a temporary preview
scene and optional prefab **clone**. Presets cover 16:9, 19.5:9, 20:9 and 4:3,
portrait/landscape, with editable L/T/R/B pixel insets. Red indicates unsafe area.
It never saves a source prefab or changes the runtime screen/scaler configuration.
Preview/check rendering deep-clones fonts, fallbacks, materials and atlas textures.
A short synchronous Editor-only TMP 3.0.7 settings-cache scope prevents global
fallbacks from populating persistent fonts, then restores the real settings even
on failure. No such reflection/settings substitution is used in the runtime or
the real-font rendering test.

## Accessibility and checks

`UIAccessibilityOptions` validates 0.75..2 font scaling, high contrast and reduced
motion. Theme high-contrast colors are explicit; the sample keeps focus independent
of color. Reduced motion uses the **existing** `UITransitionRunner`, requests its
SkipToEnd interruption for active transitions, and skips new effects to their
correct endpoint. It does not create another animation writer.

`UIAccessibilitySemantics` supplies localized label + role to an injected
`IUIAccessibilitySink` on allowed Selectable focus; the sample displays that
announcement and brings it into view. It respects existing interaction gates.
This is not an OS screen reader or full accessibility certification.
`ILocalizedTextFormatter` and `ILocalizedTextLayout` expose date/currency,
one/other key selection and processed-text/RTL integration. Full CLDR plural rules,
Unicode bidi/Arabic shaping and platform accessibility remain extension work.

`YUIFramework > Stage 11 > Check selected text content` reports missing keys,
font/glyphs and real TMP overflow with the exact component as log context.
It forces layout/mesh generation rather than counting string length. Inspect the
actual Canvas hierarchy at its intended size; alternate locales/sizes should be
checked after switching them. It is a focused Editor command, not a stage-14
global build validator.

## Requirement-to-entry-to-regression matrix

| Requirement | Actual entry | Regression |
|---|---|---|
| Config language/key/component | TextLocalizationService, ConfigLocalizedText, key drawer | Retained 14 Edit + 6 Play text/Inspector suites |
| Typed color/font/sprite/material map | UIPresentationCatalog/Service | Presentation schema, wrong type/key, JSON/bytes parity, failed export unchanged |
| Safe area/aspect/viewport | UIScreenLayout, UISafeAreaContent | Six actual RectTransform device geometries, nested scale/viewport, restore/dedup |
| Root scaling/fullscreen protection | UIRootRuntimeOptions, full layer roots | External scaler restoration, unchanged layer anchors, full sample backdrop |
| Live language/theme/accessibility | PresentationSampleView | Same page instance, badge/color/size/motion/focus assertions, 18 aspect/scale layouts |
| Real Chinese font | WenKai TMP assets/fallback | Official hash, glyph indices, mesh vertices, raster ink, bounded 300-CJK fallback test |
| Async ownership | ConfigPresentationBinding + resource leases | Out-of-order/noncooperative loads, disable/rebind, caller cancel, reload/reinit |
| Context and cleanup | DisplayToken + existing transitions | Hide/pool/close rollback/failed show/Shutdown, 1,000 disable cycles |
| Formatting/RTL extension | ILocalizedTextFormatter/Layout | Injected formatter/date/currency/plural/layout invocation |
| Editor content validation/preview | UIContentChecks, UIDevicePreview | Exact key/glyph/overflow targets, original font tables/dirty flags unchanged, preview scene disposed |

Fresh full-suite counts, timestamps and evidence manifests are in [Testing.md](Testing.md).
The raster test checks 38 visible glyphs/152 vertices and 11,383 ink pixels on the
tested Windows Direct3D11 editor. Dynamic growth is measured on a transient clone
of the real configured fallback (300 requested CJK glyphs, one atlas), so tests
do not serialize their generated glyphs into source assets. It is not device rendering/FPS, IL2CPP, all-Unicode
coverage or a presentation zero-allocation claim.
