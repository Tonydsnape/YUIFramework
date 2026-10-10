# Config tool

Run commands **in this repository**, never in the source game project.
Node >=20 is required (acceptance used Node 24.15.0). Dependencies are pinned by
`package-lock.json`: ExcelJS 4.4.0, JSON5 2.2.3, `@msgpack/msgpack` 3.1.2.

```powershell
Set-Location Config
npm.cmd ci --ignore-scripts
npm.cmd test
npm.cmd run validate
npm.cmd run export:client
npm.cmd run export:server
```

Edit `UISettings.xlsx` to maintain UI configuration. `npm.cmd run sample` creates
the initial six-record workbook with ExcelJS when it is missing; it deliberately
refuses to replace an existing workbook. No game workbook or generated business
repository was copied.

`LocalizationTextConfig.xlsx` maintains the stage-11 nine-key text sample (zh-CN/en/fr).
`npm.cmd run sample:localization` creates it only when absent. Its required typed
catalog participates in the same transaction as UISettings. Duplicate keys, unknown
locale columns, malformed positional formats and inconsistent argument indices fail
before publication. See [text localization](../Documentation/Y2.0/Localization.md)
for the schema, key picker, runtime APIs and explicit fallback policy.

`UIPresentationConfig.xlsx` adds typed theme Color/Font/Sprite/Material tokens
and locale Font/Sprite overrides to the same atomic publication. The sixteen-row
sample drives the existing Hello/verified-Bootstrap UI with WenKai TMP fonts.
See [presentation](../Documentation/Y2.0/Presentation.md) for asset addresses,
licensed font provenance, runtime switching and safe-area/accessibility controls.

## Workbook protocol

Every data worksheet has A1=`Name`, B1=table name, C1=`Type`, D1=`base` or `normal`;
A2=`Key`, B2=positive key depth. Optional C2=`Required`, D2=Excel boolean defaults true.
Empty worksheets and temporary `~$` workbooks are ignored.

| Kind | Schema | Data |
|---|---|---|
| base | Row 3: KeyName, Target, ValueType, Value; Key must be 1 | Rows 4 onward |
| normal | Row 4 targets, row 5 field names, row 6 types; first Key columns form the compound key | Rows 7 onward |

Targets are `c`, `s`, `sc`; key columns must use `sc`. Supported types are `int`,
`long`, `float`, `double`, `bool`, `string`, `string?`, `json`. Keys are int/long/string.
Blank nullable strings become null. JSON cells use JSON5 object/array syntax;
nested nonfinite numbers are rejected. Longs are signed 64-bit **decimal strings**
in both output formats. Use text cells for numbers outside JavaScript's exact integer
range. Formula/date/error/rich-text/hyperlink cells are not evaluated.

Normal tables are nested string-keyed maps; leaves include the key fields. The C#
parser verifies those fields agree with the map path. Duplicate compound keys,
case-insensitive table names, missing fields, invalid C# symbols/collisions and unsafe
filenames fail before any output is replaced. Tables are limited to 16 MiB per format,
one million value nodes and conservative depth 63. Required normal tables cannot be empty.
The JS MessagePack decoder rejects `__proto__` map keys by design; export JSON and
the C# codec preserve such own string keys without prototype mutation.

## Output and configuration

```powershell
node src\cli.js --target client --input . --project-root .. `
  --out Config\Client --generated Assets\YUIFramework\Examples\ConfigGenerated `
  --namespace YUIFramework.ConfigGenerated
```

Defaults resolve relative to this installed tool, not the invoking shell or a machine
path. `--project-root`, `--out`, `--generated`, `--input` and `--namespace` are explicit.
`--no-unity-sync` exports only the tool outputs. `--validate` performs parsing, UI schema,
code-generation and selected-format checks without writing. No game environment
variables, source-game export batches or automatic sibling-project synchronization exist.

Client export publishes six directories in one rollback transaction:

- `Config/Client/json`, `Config/Client/bytes`, `Config/Client/generated`
- `Assets/YUIFramework/ConfigData/Editor/json`
- `Assets/Resources/YUIConfig`
- the selected generated-C# directory

Only filenames listed in each `.config-owned.json` marker are managed. Markers in Assets
must be retained in version control. Existing metas and unrelated files are preserved;
obsolete managed files and their metas are removed. Unmanaged filename collisions,
overlapping/escaping paths and symlinks are rejected. A per-project export lock rejects
concurrent exporters. Injected failure at **each of the six swaps** restores the entire
previous publication. This is exception rollback, not power-loss/crash ACID: recovery
failures retain backups and the lock for manual investigation. Close the editor or suspend
automatic asset refresh while exporting; do not import/read these directories mid-swap.

## C# dependencies

Unity's manifest pins Newtonsoft JSON UPM 3.2.1. Original dependency metadata under
`Assets/YUIFramework/Config/ThirdParty` pins MessagePack/Annotations 3.1.4,
Microsoft.NET.StringTools 17.11.4, System.Collections.Immutable 8.0.0 and
System.Runtime.CompilerServices.Unsafe 6.0.0. Unity's netstandard2.1 profile supplies
System.Memory. Included DLL/package SHA256 values and upstream MIT licenses are retained.
No dynamic MessagePack model generation or Unity-type serializer package is needed for
this primitive protocol.

To restore the declared DLLs from official NuGet packages:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Restore-RuntimeDependencies.ps1
```

This script writes only the target tool cache and declared target runtime dependencies.
See [the runtime and migration guide](../Documentation/Y2.0/Config.md).
