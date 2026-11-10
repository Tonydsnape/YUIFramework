# Y2 troubleshooting

Start with the exact page, OperationId, phase, resource address/package and full
exception chain from bounded diagnostics. Preserve the first failure and cleanup
failures separately. Never replace a failed resource/collector/adapter operation
with an empty success report.

| Symptom | Check / action |
|---|---|
| New package cannot resolve UniTask/YooAsset | Merge the consumer Git dependency and OpenUPM scope from PackageQuickstart; retain its lockfile; package.json semver alone does not configure a registry |
| No Examples types after install | Expected before Package Manager **Import**; Samples~ is not compiled in place |
| No package tests visible | Add the package name to consumer manifest.testables; verify Test Framework and test-assembly asmdefs |
| Duplicate types/assemblies/GUIDs | Do not compile original Assets sources beside the installed package, or regenerate the shipped schema into another compiling Assets folder |
| Editor code enters Player | Check includePlatforms on every asmdef, not directory names; do not add sample/Editor references to Runtime |
| Export verify changes after Unity import | Missing metadata or modified package contents is an error; compare source-manifest rather than silently updating its hashes |
| Config root missing/ambiguous | Import exactly one complete sample or inject an explicit Editor JSON path/source; the runtime never searches and silently falls back to demo Resources |
| Page wizard cannot find Config/tools | Explicitly install authoring tools once in a package consumer and restore their pinned dependencies if missing |
| Generated files conflict | Keep hand partials separate; restore/reconcile edited generated ownership before rerun; never overwrite it with force |
| Build blocked by missing UI/resource | Use the profile report's asset/ObjectPath/property; correct actual package/collector/address and source closure, not just a matching string |
| Production flags demo Resources | Produce an explicitly isolated production workspace or remove application-owned demo inputs there; never move the user's login assets just to pass |
| RC bundle rebuild refuses output | Use new external version/output and a fresh consumer, or explicitly relocate only the previous owned RC builtin directory |
| RC output/report refused | Use absolute external, new, nonlinked, non-nested Player/report paths; bundle history roots may exist. Rejected requests do not start bundle builds or change settings/StreamingAssets |
| Offline package/marker missing | Build/copy the target-matched official YooAsset package into that consumer's StreamingAssets; EditorSimulate is not a Player backend |
| Offline `BuiltinCatalog.bytes` returns 404 | Versioned patch output is not the complete YooAsset 3 builtin set. Use official OnlyCopyAll/catalog generation into the new version-owned builtin directory, then copy that set; do not hand-create a success catalog |
| TMP content scan fails in fresh consumer | Non-TMP roots need no TMP defaults; TMP roots must have application settings/fonts. Import Essential Resources or explicitly create the sample's owned licensed assets |
| Device/theme preview unavailable | Supply real Config paths/fonts/materials and a supported Editor renderer; do not manufacture a zero GPU/counter result |
| `Already continuation registered` | Find two concurrent pending waits on one UniTask/Preserve; use a single consumer and shared Task backing where observation and destruction drain coexist |
| Destroy races with load/open | Cancel the owner, drain actual work, respect generation/token fences, release late leases; inspect ownership rather than assuming a canceled await stopped the provider |
| Loading never closes / closes too soon | Dispose each independent handle once; no global Hide pairing; inspect owner/token and live reference count |
| Modal/focus/input is stuck | Inspect the one runtime's modal stack and lock owners; do not disable EventSystem or overwrite CanvasGroup to bypass arbitration |
| Idle pool reported as leak | Distinguish legitimate idle entries/borrowed globals/TMP resident assets from released owner/task/lease residue |
| High IL2CPP build succeeded but code loading fails | Stock IL2CPP does not support downloaded managed DLL execution; linker retention, concrete AOT instantiations and future HybridCLR metadata differ |
| `link.xml` appears ignored | Application rules must be under Assets (the real imported Hotfix sample) or use a supported linker processor; package-internal placement is insufficient in Unity 2022.3 |
| High Player reports `Default constructor not found for type YooAsset.BuiltinFileSystem` | Verify core UIYooAssetLinker ran and inspect the actual stripped YooAsset DLL constructors. Offline/Host use reflection; moving an XML into Packages alone does not preserve them |
| RC app exits shortly after launch | Automated smoke intentionally exits after reporting success **or failure**. Inspect `[YUI-RC]`, JSON and app-specific errors; do not infer success from exit |
| APK exists but no smoke result | Verify actual install/launch, inspect the dedicated application's temporary-cache report and process/log outcome; build success is not a device result. Android smoke explicitly uses activity.getCacheDir(), not potentially external temporaryCachePath. adb can print Permission denied with exit 0: parse JSON/PNG and record access failures explicitly |
| iOS remains blocked on Windows | Supply macOS/Xcode/signing/authorized device; neither an Android APK nor a generated Xcode project closes native iOS/device gates |

Do not paste raw Android/Unity launcher environment dumps into reports. Keep raw
logs local and provide redacted review copies; do not include credentials, adb
environment variables, private device identifiers or telemetry uploads.
