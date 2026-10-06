# WC4 Map Editor

This repository builds a **Windows WPF desktop asset editor**, not the Android
game or an APK. The UI targets `net10.0-windows`. Core, Rendering and CLI target
`net10.0`; binary parsing, editing and regression tests can run on Linux.
Rendering still needs platform-native SkiaSharp libraries and the editor's
art/configuration resources.

## Build and test

Install a .NET 10 SDK. From this repository on Windows:

```powershell
dotnet build WC4MapEditor.csproj -r win-x64
dotnet run --project WC4MapEditor.Tests
```

The GUI build is under `tmp/bin/WC4MapEditor/Debug/net10.0-windows/win-x64/`.
Run `WC4MapEditor.exe` there. Keep the full output directory together; a debug
build is not a self-contained release. It needs the .NET 10 Desktop Runtime.
The source tree is missing some original artwork: provide a matching `Resource/`
tree, `setting.txt` and any required `Maps/` beside the executable before GUI
acceptance testing. Configuration lookup also searches ancestors when developing
inside the source tree. Missing optional application icons do not block builds.

Linux build/test commands:

```bash
dotnet build WC4MapEditor.Cli/WC4MapEditor.Cli.csproj
dotnet run --project WC4MapEditor.Tests -- --corpus /path/to/game
dotnet build WC4MapEditor.csproj -r win-x64 -p:EnableWindowsTargeting=true
```

The last command cross-compiles Windows code; it does not run WPF on Linux.
`--corpus` is optional and expects the sibling game research repository, including
`wc4/` and `map_5.13_Dev/Maps/world2.bin`. Without it, synthetic regressions run.
No commercial game fixtures or credentials are copied into this repository.
Build outputs and test files go under ignored `tmp/`; existing tracked historical
`obj/` files are excluded from compilation.

## Map I/O repairs (2026-09-13)

- `WorldParser` reads/writes the native YSAE/v4 header with 32-bit LE dimensions,
  the 16-byte terrain plane and the separate 2-byte province/tail plane. Magic,
  dimensions and exact file length are validated before allocating map state.
- `BtlLayout` and `BTLParser` implement a single versioned binary codec. Stage
  and Conquest APIs delegate to it. Terrain uses actual area; province/owner
  planes use header capacity with separate padding. Version 2, like version 3,
  uses 64-byte armies and 104-byte reinforcements.
- Reserved model bytes, opaque `0x6c` records, v3 extra data, placement A/B
  boundaries and zero-capacity legacy headers survive no-op roundtrips.
- Saves serialize and validate before writing a same-directory temporary file,
  then replace the destination. Invalid data does not truncate an existing file.
- Non-square conquest creation preserves width/height. Resize and undo now cover
  terrain, provinces, owners, objects, header and opaque metadata, not just cells.
- The address analyzer uses the same section layout as saving. Detailed world
  JSON export no longer stops at 10000 cells; it includes full `province_value`
  and terrain reserved bytes. Exported int coordinates are not narrowed to short.

CLI JSON is an inspection format, **not** a lossless BTL interchange format or
JSON-to-BTL importer. Some old summary names remain for compatibility (for
example `country_id` is the low byte of a province value). Use binary codec
roundtrips, not JSON summaries, for preservation checks.

```bash
dotnet run --project WC4MapEditor.Cli -- world world.bin --detailed -o world.json
dotnet run --project WC4MapEditor.Cli -- conquest conquest1.btl --analyze
```

## Limits and transform policy

These are editor safety budgets and on-disk field limits, **not evidence of
Android gameplay support**:

| Constraint | Value / behavior |
|---|---|
| Positive dimensions | Required; checked area at most 1000000 cells |
| Input/output BTL/world file | At most 256 MiB; exact known layout required |
| Resize peak estimate | At most 256 MiB; oversized operations fail before mutation |
| Resize history snapshots | Budgeted separately at 256 MiB; oldest history is dropped |
| Unit/trap placement | Non-negative signed-short indexes 0..32767 |
| Building placement | Unsigned-short indexes 0..65535 |
| Province targets during remap | 0 and 65535 remain sentinels; new positive targets stay in signed range |
| Scale factor | Finite 0.1..10; destination cells use nearest-cell resampling |

The old world creation UI limit of 10..500 per side is replaced by shared area
validation. It is not a promise that every editor feature supports every shape
inside that budget. Whole-map army/trap generators reject areas above 32768
before changing anything; manual placement can still use encodable cells.

Standalone world and map-ID-0 BTL resize/scale remap known coordinates and
province targets. Cropped independent objects are removed. Cases and capital
list slots are retained with -1 for cropped targets. Placement A/B remain
separate. Object collisions, narrowed coordinates and ambiguous references
fail transactionally. Unknown field semantics are not guessed or rewritten.

**World-backed BTLs (map ID not zero) support only bottom append with unchanged
width/origin here.** Left/right/top changes, cropping and scaling require a
linked-world transaction and are deliberately rejected. Nonempty opaque/extra
sections also block coordinate-changing transforms; bottom append retains them.

The editor console uses `resize_map down 10 ocean` and `scale_map 1.5`; errors
are reported instead of unconditional success. Successful changes can be undone
and redone with complete snapshots. Binary helpers can be used without the UI:

```csharp
var map = BTLParser.LoadFromFile("conquest1.btl");
MapTransform.Resize(map, map.MapWidth, map.MapHeight + 10);
BTLParser.SaveToFile(map, "conquest1-expanded.btl");
```

Appending a BTL does not extend its referenced world, image atlases, labels or
camera bounds. Extend and preflight those companion resources separately.
The sibling game repo's `patches/wc4/map-height-148x60/` is a non-release candidate,
not a complete southern-hemisphere map. Never overwrite the only original files.
No Android SO is changed by these editor repairs.

## Unit and general repairs (2026-09-15)

The editor was cross-checked against the sibling game's decrypted 1.28.0,
modded 1.26.0/1.28.0 and integrated assets, not just its bundled configuration.

- General editing preserves ID 0, null strings/lists, omitted properties and
  unknown JSON fields. Loaded numeric values are not clamped to old UI limits.
  `ResetSkills` is a count (real values include 5 and 10), not a boolean.
- List selection/deletion targets the actual row. Existing duplicate general
  IDs are retained rather than silently merged; auto-assignment excludes these
  ambiguous IDs. Existing IDs cannot be edited without migrating references.
- Skill/medal text rejects malformed, negative or overflowing IDs. Numeric
  controls reject nonfinite input and fractions for integer fields. The editor
  does not silently drop tokens or save after a failed GeneralSettings load.
- JSON saves overlay only changed fields on each original object. Portrait XML
  retains unknown nodes/attributes. `SaveAll` validates/serializes both files,
  stages both, and restores replaced files on a recoverable write failure.
  This is not a crash-atomic database transaction; keep independent backups.
- `Photo` is preferred over `EName` for portrait lookup/editing. Deleting a row
  keeps shared portrait entries. Preview loads are cancelled when selection
  changes, including cache-hit and missing-image paths.
- New generals use an unused positive ID, six-star maximum fields and five
  editable skill slots by default. Shared EName/Photo is allowed. This only adds
  the general JSON row and portrait placement, not artwork or game unlocks.
- Parsed catalogs refresh on resource scan/clear, forced reload and editor save.
  String-table caches are keyed by locale; UTF-8 BOMs are accepted. Reloading
  the general editor rebinds to the active assets root; stale-root saves fail.
- Unit lookup uses `ArmySettings.Army`, not the full record `Id`. Specialties
  use `Type`: 1 infantry, 2 armor, 3 artillery, 4 navy, 5/13 aviation. Generic
  formation operations use the minimum `MaxFormation` across variants when
  country/era context is unavailable (e.g. code 19 carrier=1, other ships=2).
  Display names are representative rows, not a complete country/era resolver.
- Skill levels come from `SkillSettings.Level`: ID 1320 can be Level 10 and ID 0
  is an empty slot. Random templates operate on the actual Skills list and
  choose existing records of the same `Type` at the requested level. Bundled
  specialty pools now contain real skill IDs, replacing invalid seeds 1..50.
- Auto-assignment clears only the requested ownership group and resynchronizes
  used IDs from both army and reinforcement formats. It deduplicates candidates,
  writes `MilitaryRank` to `Rank` (not v1 `Nobility`), clears unused skill levels,
  and refuses invalid catalogs before clearing units. Count 0 is a no-op;
  positive counts remain per ownership group, matching the existing workflow.

### Data boundaries and use

Work on a **copy of assets**, scan it, then reload the general editor. Save
before returning to map auto-assignment, which reads persisted general data.
If the active resource root changes, reload before saving. External JSON edits
need a forced rescan/reload to refresh parsed catalogs.

`SkillsMax=10`, `ResetSkills=10` and ten JSON skill IDs can be preserved/edited.
This does **not** add ten BTL skill fields or patch the game's UI/SO. Automatic
assignment skips >5-skill records, missing skills, ambiguous general IDs,
general IDs outside 1..32767 and ranks/levels outside the BTL byte range.
Syntactically valid JSON IDs are not globally migrated. Use the explicit
relationship audit below to check its documented subset of references.

Deleting a general does not rewrite BTLs, promotions, titles, rewards or shop
tables. Adding a general does not automatically add it to the editor's
`Resource/Config/GeneralInCountry.json`; add its ID to the intended country's
`Generals` list and restart the editor for auto-assignment. That country list
is editor configuration, not a game unlock mechanism. Custom units still need
matching editor/game artwork; a new JSON ID alone cannot supply sprites.

Windows acceptance still required: edit an unrelated property of ID 0 and a
ResetSkills=10 row; select the second duplicate ID; reject invalid tokens;
edit a Photo!=EName portrait; rapidly switch cached/missing portraits; apply
each template; then assign a general to one country and compare other countries.
Perform these checks on copies, not the only original files.

## Relationship audit (2026-09-20)

The asset browser now preserves the selected root on entry/refresh and provides
a folder picker and **数据检查** action. The general editor toolbar provides
**检查数据 / 查询引用**, including after deleting the selected row. This entry
checks an independent snapshot of unsaved general edits; it does not save them.

The audit window provides diagnostics by severity, incoming references by
general ID, optional base assets and an optional BTL scan. Changes to the base
root or BTL checkbox take effect on the next **检查**. Export creates a complete
JSON report, regardless of the active view/filter. English diagnostic codes and
field names are retained for scripting. Large reference grids are virtualized;
audits run in the background and cancel when the window closes.

CLI equivalents, from this repository:

```bash
dotnet run --project WC4MapEditor.Cli -- asset audit /path/to/assets
dotnet run --project WC4MapEditor.Cli -- asset audit /path/to/overlay/assets \
  --base /path/to/base/assets --include-maps
dotnet run --project WC4MapEditor.Cli -- asset audit /path/to/overlay/assets \
  --base /path/to/base/assets --general 1903 --output tmp/general-1903-audit.json
```

- Roots may be `assets/` or a parent containing `assets/json/`. Base resolution
  is for the auditor, not an automatic merge/save into the editor's active root.
- Overlay files replace the whole base file; rows are never merged by ID.
  Invalid overlay JSON/BTL does not silently fall back to the base file.
- 32 explicit JSON relationships cover general skills/promotions/titles/stages,
  unit features/buffs/elite/item/skin links and selected reward/shop references.
  Family keys such as `Army`/feature `Type` allow multiple variant rows.
- Checks include missing/ambiguous IDs, malformed references, chain cycles and
  mismatched owners, feature/level pairs and five-slot BTL compatibility risks.
  Missing target tables yield `unchecked` references, not proven missing IDs.
- `--include-maps` scans deployed armies in effective `stage/**/*.btl` files
  using the versioned codec. **Reinforcement field meanings remain unverified**;
  these records are counted but excluded from reference assertions. Lossless
  binary roundtrip does not establish the meaning of every model field.
- `--general` filters text only. JSON contains all diagnostics, references,
  table paths and coverage notes. Rows are 1-based; array indices are 0-based.
  Exit codes: 0 = no error diagnostics (warnings may remain), 1 = errors found,
  2 = invocation/read/export failure. Parser argument failures may return 1.
- Reports require a new filename outside both input roots. Existing targets
  and directory-link output paths are refused. No game files are changed.

This is a manual audit, not a save/delete gate or a reference migration tool.
No result can establish that deleting a general is safe across unmodeled events,
reinforcements, native code, saves or image/XML/localization references. Missing
fields are not schema-inferred; supply the full matching base for partial packs.

The integrated overlay plus modded 1.28.0 base yields 81 effective tables and
1399 maps: 17 errors, 26 warnings, 158559 checked/indexed references and 72224
reinforcement records explicitly excluded. The 17 errors are inherited source
rows: 15 nonempty feature/level length mismatches and 2 missing buff IDs
1033/1404. Warnings cover 4 duplicated general IDs, 20 ambiguous references
(2 JSON, 18 BTL) and 2 >5-skill records. These are consistency diagnostics, not
proof of game crashes. No original row is repaired automatically.
Details and the exact reproduction command are recorded in the sibling game's
`docs/MAP_EDITOR_ANALYSIS.md`.

## Verification status

The .NET 10.0.401 Linux run passes 48 test groups, including 4092 real BTL
byte-identical roundtrips, original worlds, randomized versioned records/padding,
all modeled coordinate collections, crop/undo/redo, neutral owner append,
overflow/collision refusal, a 10349-cell CLI export and v2 int-coordinate export.
Four real general tables (4810 rows) pass semantic JSON roundtrips and portrait
XML preservation; original files are not written. New fixtures cover caches,
locales, malformed input, multi-file staging failure, duplicate/shared records,
real Level 10 skills, extended unit codes 104..114 and both assignment formats.
Audit fixtures also cover overlay precedence/no fallback, null/BOM/malformed
input, ID 0, duplicate IDs, feature/chain checks, independent unsaved snapshots,
all three BTL versions, skipped reinforcement coverage, cancellation, output
guards (including Linux symlinks) and CLI exits/complete JSON export.
Core/CLI and Windows WPF win-x64 cross-target builds pass; the final incremental
Windows build reports 0 warnings/0 errors, while clean builds have older warnings.

Not validated: Windows GUI rendering/input/save workflows, a standalone release
with all artwork, Android camera/pathfinding/turns/saves or game support for
arbitrary larger dimensions. Unknown data is preserved, not fully understood.
Legacy standalone `*Module`/`*Offsets` helpers are not the active save/layout
path; new callers should use `BTLParser` and `BtlLayout`.

The repair log and original review live in the sibling game repository:
[working memory](../game/docs/WORKING_MEMORY.md) and
[editor analysis](../game/docs/MAP_EDITOR_ANALYSIS.md).
