# Config-driven text localization

This is the text part of stage 11; [Presentation.md](Presentation.md) describes
screen adaptation, localized assets, themes, fonts and accessibility.
It follows the source project's table/key picker and enable/disable refresh pattern,
but uses original instance-owned YUI code: no Unity Localization, Addressables,
static mutable language database or SmartFormat. Source game code/data is not imported.

## Maintain and export text

Edit `Config/LocalizationTextConfig.xlsx`, worksheet `Text`.
The original four keys remain:
`sample.greeting`, `sample.title`, `sample.switch`, `sample.fallback`.
Stage 11 adds `sample.theme`, `sample.size`, `sample.contrast`, `sample.motion`
and `sample.close`, for nine rows total.
Its columns are `Key` (`string`), `zh-CN`, `en`, `fr` (`string?`), all `sc`.
It is a required normal table with one key column. Data begins on row 7.
`sample.fallback` intentionally has an empty French translation.

```powershell
Set-Location D:\mywork\YUIFramework\Config
npm.cmd test
npm.cmd run validate
npm.cmd run export:client
```

`npm.cmd run sample:localization` recreates the original test workbook **only if
it is missing**; it refuses to overwrite maintained translations. Client export
validates and publishes UISettings, LocalizationTextConfig and UIPresentationConfig in the existing
six-directory rollback transaction. All six UISettings records and their JSON/bytes
remain unchanged. Export only while Unity asset auto-refresh is suspended/the
editor is closed; the transaction does not isolate a concurrent asset importer.

The initial supported locale contract is exactly **en, zh-CN, fr**; en and zh-CN
columns are required, fr is optional. This bounded allowlist is checked both by
`Config/src/localization-schema.js` and `LocalizationTextCatalog.SupportedLocales`.
Adding another locale means extending both allowlists and testing that locale on
the intended Unity runtime before authoring its column. A spelling-shaped but
unrecognized locale must not replace a usable exported publication.

Keys are ordinal, case-sensitive, unique, nonempty, and trimmed. Blank/whitespace
translation cells mean *missing*, not an intentional empty displayed string.
Use `{0}` through `{15}` for positional arguments and `{{` / `}}` for literal
braces. Every nonblank translation of a key must reference the same set of indices.
Named arguments, alignment, format specifiers, inline plural rules and SmartFormat are
rejected. Missing arguments fail at lookup; extra arguments are harmless.
An injected `ILocalizedTextFormatter` provides explicit Date/Currency/PluralKey
extension points; `GetPlural(oneKey, otherKey, count, args)` selects a key rather
than interpreting a plural DSL. The default selector is one/other, not CLDR.
`ILocalizedTextLayout.Prepare` can supply processed text and a TMP RTL flag.
The default is LTR; complete bidi/Arabic shaping remains application-owned.

## Typed table and code API

Generated `ConfigGenerated.LocalizationTextConfig.Table` aliases the one
`LocalizationTextCatalog.Table` descriptor. The catalog/parser lives in the
separate `YUIFramework.Localization` assembly, so it does not depend on Examples.
The base Config assembly has no localization/UI dependency.

```csharp
// Use the same ConfigService as the UI configuration startup.
var text = new TextLocalizationService(configs); // locale zh-CN, fallback en
await configs.InitializeAsync(token);

var catalog = configs.Snapshot.Get(LocalizationTextCatalog.Table);
LocalizedTextEntry row = catalog.Get("sample.title");
bool exists = catalog.TryGet("sample.title", out var other);

string greeting = text.Get("sample.greeting", "Ada");
text.SetLocale("en"); // synchronous, event-driven refresh of bound components
bool exact = text.TryGet("sample.greeting", out var value, out var detail, "Ada");

text.SetLocale("fr");
LocalizedTextResult fallback = text.Resolve("sample.fallback");
// fallback.Status == Fallback; RequestedLocale == "fr"; ResolvedLocale == "en".
// The caller explicitly decides whether to display fallback.Text.
```

`Get` throws `LocalizedTextException` unless the result is **Exact**.
`TryGet` returns false and a null text for every non-exact result, including
fallback; its result parameter preserves diagnostics and any resolved fallback.
`Resolve` returns one of Exact, Fallback, NotReady, MissingKey,
MissingTranslation, UnsupportedLocale, InvalidFormat, or Disposed.
Only Exact/Fallback have `HasText == true`. There is no silent key-as-success API.

Fallback is one hop to the configured fallback locale (en by default), never an
implicit region-stripping chain. Formatting uses the **resolved** locale's culture.
A format failure is not hidden by trying another language. Unknown locales are
rejected before changing the current locale. A supported locale can be selected
before Config is ready; if a later catalog omits that optional column, lookups
explicitly return UnsupportedLocale until the caller chooses a present locale.

The catalog, entries, keys, locales and translation dictionaries are read-only.
Every lookup reads ConfigService's currently published snapshot; there is no second
mutable copy. Invalid schema/format/row-path identity fails during parsing before
**any** table in that load publishes. A failed reload preserves the previous
snapshot and displayed content. A successful reload refreshes text without
re-registering UI types or invalidating existing pools.

## Unity component and Inspector

1. Add `ConfigLocalizedText` beside a uGUI `Text` or `TMP_Text` component.
2. In **Text Key**, open the searchable dropdown and choose an exported valid key.
   The Inspector shows a preview; missing exports/unknown existing keys are explicit
   errors/warnings, not a silent free-text substitute. Selection supports
   multi-object editing and Undo, and revalidates the catalog when applying.
3. Set the optional ordered string **Arguments** array, or call `SetArguments`.
4. Inject the application-owned service. Components do not find/create a global
   service or load config implicitly:

```csharp
// Context.HandleShow: the binding automatically follows DisplayToken.
TrackDisplayBinding(label.Bind(textService, this));

// Scene-owned UI without a Context:
IDisposable binding = label.Bind(textService);
// Dispose this handle at the scene-owner boundary; OnDestroy also detaches it.
```

Binding before Config finishes is supported: text is empty while NotReady, and
the first successful publication refreshes it. Disable detaches event listeners;
re-enable refreshes from the latest snapshot. Rebind disposes the former binding,
and disposing an old handle cannot remove its replacement. Destroyed Unity objects
are treated with Unity fake-null semantics.

Context bindings end on actual display cancellation (Hide, Pool, Close,
failed-open cleanup, or Shutdown), and must be rebound on the next HandleShow.
A canceled close rolling back to Opened keeps the original valid display binding.
Covered/suspended contexts defer text updates and refresh when resumed; Config
shutdown/disposal still clears stale text even while suspended.
When showing an already-open page again, replace that page's binding/command group
instead of accumulating display listeners. The runnable sample does this explicitly.

Components display fallback text with a warning; missing key/translation/locale
or invalid format logs an error and displays the key, with the exact status exposed
as `LastResult`. NotReady/Disposed clears text without a spurious error.
Components cache control references and never poll in Update.

## Startup, resources and ownership

`HelloUIBootstrap` now runs the existing Config/UI startup chain and opens
`SampleHelloPage` with `SampleLocalizedHelloArgs`. Its greeting and new language
button use table keys; with `UIPresentationService` it shows the TMP presentation
sample and cycles en/zh-CN/fr. Other pre-existing sample
labels are not presented as already translated. A plain-string page argument is
still compatible. Returning through navigation with null args preserves the
borrowed localization service; closing releases it.

`HotUpdateStartupSample` composes the same service only after resource verification:
**Bootstrap ready -> Config Initialize -> validated UI batch -> localized page**.
Failure prevents registration/business entry. Generic Bootstrap users are not
forced to enable localization.

EditorSimulate uses
`Assets/YUIFramework/ConfigData/Editor/json/LocalizationTextConfig.json`.
Production YooAsset uses a MessagePack TextAsset at **packageName +
`LocalizationTextConfig`**, alongside **`UISettings`** and **`UIPresentationConfig`**. Collect all three `.bytes` files
from `Assets/Resources/YUIConfig` with filename-without-extension addresses;
adjust the existing explicit ResourceConfigSource prefix/package if using another
collector convention. The minimal CodeView Player sample explicitly uses
`ResourcesResourceProvider` at `YUIConfig/LocalizationTextConfig`, not a production
fallback. No implicit JSON/resources fallback masks a missing bundle.

Owner teardown is **UI -> presentation Dispose -> text localization Dispose -> Config Shutdown -> resources ->
Bootstrap**. Localization borrows Config and never shuts it down. Config borrows
resources and releases table leases before publication. Disposing localization
detaches Config notifications even when a listener throws; a fresh instance can
be created after owner reinitialization. Config shutdown/reinitialization can
also reuse a still-owned localization service.

All mutation, lookup, subscription and Unity control work belongs on the owning
Unity thread. `Changed` and `ConfigService.SnapshotChanged` dispatch a captured
invocation list in subscription order; add/remove affects subsequent dispatches.
All observers run even if one throws, then errors aggregate. Language/snapshot
changes are already committed when a notification fails. Config exposes
`LastNotificationFailure` separately from data-load `LastFailure`; Initialize/Reload
still surface the notification error, so startup cannot silently continue.
Shutdown aggregates notification and drain failures while completing cleanup.
Mutation of the currently notifying service is forbidden, including across nested
Config notifications; listeners should schedule subsequent owner actions instead.

## Verification boundaries

See [Testing.md](Testing.md) for fresh XML intervals, counts, manifests and regressions.
Tests include actual Text/TMP text assignment, before-ready enablement, language/
reload/reinit, Inspector multi-edit/Undo, strict fallback/errors, delayed canceled
loads, observer reentrancy, 1,000 display/pool cycles, close rollback and failed show.

The completed font path uses the licensed WenKai Regular TTF, persisted TMP assets,
bounded fallback atlas, TMP settings and required TMP shader/line-breaking resources.
Text tests no longer inject temporary settings. Real Chinese/Latin/French glyph,
mesh and Direct3D raster evidence is described in [Presentation.md](Presentation.md).
The legacy plain uGUI path still has platform-dependent built-in-font coverage.
No CDN, mobile, IL2CPP, device FPS or localization zero-GC claim is made.
