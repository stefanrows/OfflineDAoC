# Client UI XML reference

How the DAoC client (`game.dll`) reads its UI XML: every element type, every
child element each parser reads, the value mappers, and the full stock
OnClickEvent table. Use this before editing a window under `ui/<skin>/`, and
to decide whether a UI idea needs only XML or also a `game.dll` patch.

**Analysis only.** Nothing here changed a client file, patch, or XML file. No
client or server was run.

## Provenance and evidence labels

- Binary: the raid baseline `game.dll`, SHA-256
  `67dcf68a37b95a93946a943b99d5e19b4a03e08cd6469275e25c7b909de21e99`, taken from
  `D:\Games\OfflineDAoC-backups\companion-custom8-probe-*` and analyzed as a
  read-only copy outside the game installation (see
  [Static client analysis](DEVELOPMENT.md#static-client-analysis-with-rea-and-ghidra)).
  This build already contains the raid patch (the `.raid` section and the
  ControlId hook at `0x4E99E6`). It does **not** contain the Companion Manager
  patch.
- Tools: disassembly with capstone, string and pointer scans of the PE image,
  and Ghidra 12.1.4 pseudocode through REA 6.4.0 for the helper functions.
  The tables below come from scripts that walk each parser's
  `xmlStrcasecmp` chain, follow the jumps into shared tails, and record the
  helper and the destination offset. The ID tables were extracted twice by
  independent scripts, and both extractions agree.
- Stock XML: `runtime/client-opendaoc/app/ui/{atlantis,isles}/*.xml` and
  `app/pregame/`, read-only. Files this fork writes or edits are **not**
  counted as stock: `custom8_window.xml`, `custom9_window.xml`,
  `custom10_window.xml` and `ui/uimain.xml`. XML comments are ignored, as
  libxml2 ignores them. `command_window.xml` and `new_group_window.xml` may be
  fork-edited, but no keyword below is confirmed only by those files.
- Labels: **confirmed (n)** means the child element occurs *n* times under
  that element type in stock XML. **decompiler only** means the parser reads
  it but no stock file uses it. Neither label proves runtime behavior; real
  client checks remain the owner's.
- Addresses are virtual addresses in the 0x400000 image. "Check" is the
  `push [keyword]` that starts the keyword's compare.

## 1. Document structure

The XML keywords are **child elements**, never XML attributes. A button is

```xml
<ButtonDef>
  <ControlId>1020</ControlId>
  <Position><X>10</X><Y>6</Y></Position>
  <TemplateName>button_left</TemplateName>
  <Label>SET</Label>
  <OnClickEvent>ChatRenameOK</OnClickEvent>
</ButtonDef>
```

No stock file puts a keyword in an XML attribute, and no parser reads XML
attributes. All element names are compared with `xmlStrcasecmp` (`0x6DDD8E`),
so names are case-insensitive (`Onclickevent` = `OnClickEvent`). Unknown child
elements are skipped silently.

### 1.1 Loader

| Step | Address | Behavior |
|---|---|---|
| In-game load | `0x4B9230` | loads `ui/uimain.xml`; the include directory is `ui/<theme>` (theme from `[0x1494E80]`: 1 minimal, 2 classic, 3 atlantis, 4 albion, 5 midgard, 6 hibernia, 8 custom, anything else isles; the fall-through for 7 is ambiguous, `0x4B917E`-`0x4B9190`) |
| Pregame load | `0x441AAC` | loads `pregame/uimain.xml` |
| Wrapper | `0x4E38A0` | reorders arguments for the loader |
| Loader | `0x4F6FE7` | `xmlParseFile`, then walks the root's children. The root element's name and its `ID` are never checked: stock uses both `<XML ID="DAOCUi">` and `<Root_Element ID="DAOCUi">` |
| Missing file | `0x4F70DE` | logs `File '%s/%s' was not found, defaulting to ...` and retries from `ui/Atlantis` |

`<Include>file.xml</Include>` (`0x4F7152`) loads `<include dir>/file.xml`
recursively. A file that is already loaded is skipped with an error log
(`0x4F2342`). Asset paths in `Font`, `TTFFont`, `Texture` and `Canvas` are
prefixed with the resource directory (`ui/`).

### 1.2 Top-level elements

The loader compares each root child against these 34 names (`0x99C1CC`-`0x99C250`)
and calls `handler(node, output)`. "Stock" is the number of stock files that
use the element at top level.

| Element | Handler | Stock files |
|---|---|---|
| `Include` | inline, recursive | 3 |
| `TTFFont` | `0x4F25CD` | 1 |
| `Font` | `0x4F2855` | 2 |
| `GdiFont` | `0x4F2985` | decompiler only |
| `Texture` | `0x4F2B8F` | 15 |
| `Canvas` | `0x4F2C88` | 10 |
| `ButtonTemplate` | `0x4F2D3A` | 7 |
| `VerticalResizeImageTemplate` | `0x4F3134` | 4 |
| `HorizontalResizeImageTemplate` | `0x4F336F` | 8 |
| `FullResizeImageTemplate` | `0x4F35AA` | 5 |
| `VerticalScrollbarTemplate` | `0x4F3932` | 2 |
| `MenuTemplate` | `0x4F3C26` | 4 |
| `ExpandableMenuTemplate` | `0x4F40A3` | decompiler only |
| `ComboBoxTemplate` | `0x4F42DA` | 4 |
| `ListBoxTemplate` | `0x4F47EB` | 11 |
| `TextAreaTemplate` | `0x4F4FB8` | 4 |
| `StatusBarTemplate` | `0x4F52C7` | 6 |
| `ImageAreaTemplate` | `0x4F5461` | 25 |
| `StatusIconTemplate` | `0x4F557E` | 8 |
| `IconTemplate` | `0x4F572A` | 2 |
| `ChatControlTemplate` | `0x4F58B8` | 2 |
| `CheckBoxTemplate` | `0x4F59BA` | 8 |
| `VerticalStatusBarTemplate` | `0x4F5E4C` | 2 |
| `CompassControlTemplate` | `0x4F5FE6` | 2 |
| `TreeControlTemplate` | `0x4F60C3` | 5 |
| `IconSetTemplate` | `0x4F632C` | 2 |
| `WindowTemplate` | `0x4ECE67` | 140 |
| `EditBoxTemplate` | `0x4F6421` | 3 |
| `HorizontalSliderTemplate` | `0x4F66DC` | 2 |
| `VerticalSliderTemplate` | `0x4F6831` | 2 |
| `ImagePickerTemplate` | `0x4F6986` | pregame only |
| `TabsTemplate` | `0x4F6B1D` | 3 |
| `HorizontalResizeButtonTemplate` | `0x4F6C8B` | 2 |
| `ListBoxHeaderTemplate` | `0x4F4493` | 2 |

`WindowDef`, `Window` and `FontDef` do not exist in this client; a window is a
top-level `<WindowTemplate>`. The keyword tables also have unreferenced
duplicates (`0x99B068`-`0x99B5FC`, `0x99BC0C`-`0x99BC90`, `0x99C78C`-`0x99C818`).
A full-image pointer scan found no code that reads them. Only the live copies
listed here matter.

## 2. Value types (mappers)

Each keyword branch fetches the element text with `xmlNodeGetContent`
(`0x6DDD7C`) and passes it, or the element node itself, to one of these
helpers.

| Helper | Value type | Accepted syntax | Unknown or empty input | Numbers? |
|---|---|---|---|---|
| `0x7720C0` | int | MSVC `atol`: leading whitespace, optional `+`/`-`, decimal digits up to the first non-digit. No hex (`0x1F` gives 0), no overflow check | 0 | yes |
| `0x4EA06F` | **event** (`OnClickEvent`, `OnShiftClickEvent`, `OnHeaderClickEvent`, `Onchangeevent`) | one of 364 stock names (§6), ASCII case-insensitive, whole string; or text whose **first byte** is a digit, then `atol` | -1 (also for `Unspecified`) | yes, if it starts with a digit |
| `0x4E99E6` | ControlId | 14 stock names (§5.1) or first byte a digit, then `atol`. On this build the entry is a raid hook (§5.1) | -1 | yes, if it starts with a digit |
| `0x4E9B50` | WindowId (window only) | 55 names (§5.2) or first byte a digit, then `atol` | -1 | yes, if it starts with a digit |
| `0x4ECB86` | point | the element's own `<X>` and `<Y>` children, each `atol`. Both are zeroed first; other children are ignored | 0, 0 | yes |
| `0x4ECBF6` | alignment flags | the element's child **names**: `TopLeft` 0, `CenterHorizontally` 1, `CenterVertically` 2, `OffsetBottom` 4, `OffsetRight` 8, `GrowWidth` 0x1000, `GrowHeight` 0x2000, OR-ed together. Child text is never read | 0 | no |
| `0x4F239C` | color | children `<R>`, `<G>`, `<B>`, `<A>`, each `atol` times 1/255 (`0x9674A4`), stored as floats | channel left unchanged | integers 0-255 only |
| `0x4F259B` | bool | text equals `true`, ASCII case-insensitive (`0x93E2C0`). `1`, `yes` and `false` all give 0 | 0 | no |
| `0x4EC159` | short string | `strncpy` of 32 bytes into a fixed field, byte 31 forced to NUL: **max 31 characters** | empty | n/a |
| `0x4ECA97` + `0x4EC159` | short text | node text converted from UTF-8 (to Latin-1 or the ANSI code page, depending on `[0x104B3D8]`) into a static buffer `0x246E680`, then the 31-character copy | empty | n/a |
| `0x404D9A` | long string | `std::string::assign`, no length limit | empty | n/a |
| `0x4ECA97` + `0x404D9A` | long text | UTF-8 conversion, then `std::string` | empty | n/a |

Notes:

- The event, ControlId and WindowId mappers test only the first byte with
  the CRT digit check (`0x770FBE`). `" 5"`, `"-5"` and `"+5"` therefore give
  -1, while `"5abc"` gives 5. The event path is at `0x4EC13F`-`0x4EC158`.
- `<GrowWidth>false</GrowWidth>` still sets the GrowWidth flag (6 stock
  uses), because the alignment helper ignores the text.
- `0x53E365`, called after every control parser's loop, is not a value
  mapper. It adds the finished control to its window (`0x515737`). Its
  internals are not decoded.
- Object allocation goes through `0x769B30` → `0x76B640(size, 0, 0)`. Whether
  that heap zero-fills is not established (a flag bit is set only when
  `[0x9953A0] & 8`). "not set" in the tables means the constructor does not
  write the field, so its default is undefined.

## 3. Window element: `WindowTemplate`

Parser `0x4ECE67` (object `new(0x9C)`). It reads these window children, then
dispatches the 36 control element types (§4) in the same child loop
(`0x4ED285`-`0x4ED6EF`). `Name` and `TabName` both set the same local
(`[ebp-0x14]`, `0x4ECECD` and `0x4ED278`). After the loop the window is
registered under that text (`0x4E3D6C`, `0x4ED716`). A window with neither
is passed to `0x4E5AAE(obj, 1)` (`0x4ED70C`); this is most likely a destroy,
but the call is not decoded.

| Child element | Value type (mapper) | Stored at | Stock XML | Check |
|---|---|---|---|---|
| `Name` | string, max 31 chars (`0x53e3c1`) | - | confirmed (142) | `0x4eceb2` |
| `CloseButton` | bool: text `true` (any case) = 1 (`0x4f259b`) | byte +0x78 | confirmed (132) | `0x4ecedd` |
| `CloseButtonOffset` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0x94 | decompiler only | `0x4ecf06` |
| `MoveButton` | bool: text `true` (any case) = 1 (`0x4f259b`) | byte +0x79 | confirmed (132) | `0x4ecf41` |
| `TopRightResizeButton` | bool: text `true` (any case) = 1 (`0x4f259b`) | byte +0x7c | confirmed (20) | `0x4ecf6a` |
| `BottomRightResizeButton` | bool: text `true` (any case) = 1 (`0x4f259b`) | byte +0x7b | confirmed (42) | `0x4ecf93` |
| `BottomLeftResizeButton` | bool: text `true` (any case) = 1 (`0x4f259b`) | byte +0x7a | confirmed (42) | `0x4ecfbc` |
| `ResizeButtonOffsetX` | int (atol) (`0x7720c0`) | +0x80 | confirmed (29) | `0x4ecfe5` |
| `ResizeButtonOffsetY` | int (atol) (`0x7720c0`) | +0x84 | confirmed (20) | `0x4ed01d` |
| `Width` | int (atol) (`0x7720c0`) | +0x60 | confirmed (142) | `0x4ed047` |
| `Height` | int (atol) (`0x7720c0`) | +0x64 | confirmed (143) | `0x4ed06e` |
| `TitleWidth` | int (atol) (`0x7720c0`) | +0x58 | confirmed (131) | `0x4ed098` |
| `TitleHeight` | int (atol) (`0x7720c0`) | +0x5c | confirmed (131) | `0x4ed0c2` |
| `ResizeableWidth` | int (atol) (`0x7720c0`) | +0x68 | confirmed (114) | `0x4ed0ec` |
| `ResizeableHeight` | int (atol) (`0x7720c0`) | +0x6c | confirmed (114) | `0x4ed116` |
| `ResizeableTwoWayWidth` | int (atol) (`0x7720c0`) | +0x70 | confirmed (100) | `0x4ed140` |
| `ResizeableTwoWayHeight` | int (atol) (`0x7720c0`) | +0x74 | confirmed (100) | `0x4ed16a` |
| `WindowId` | WindowId: name or leading-digit number (`0x4e9b50`) | - | confirmed (121) | `0x4ed194` |
| `MinWidth` | int (atol) (`0x7720c0`) | +0x90 | confirmed (100) | `0x4ed1ce` |
| `MinHeight` | int (atol) (`0x7720c0`) | +0x8c | confirmed (100) | `0x4ed1fb` |
| `ContextTemplateName` | string, unbounded (std::string) (`0x404d9a`) | +0x3c | confirmed (19) | `0x4ed228` |
| `TabName` | string (tab name, 0x426d50) (`0x53e37c`) | - | confirmed (5) | `0x4ed25b` |

## 4. Control element types

36 control types, dispatched in this order from the window parser. Every
control has the base fields from constructor `0x4EC177`: ControlId `+0x8`
(-1), Position `+0xC`/`+0x10` (0, 0), Alignment `+0x14` (0), ToolTipID
`+0x18` (0), Priority `+0x1C` (0). The type ID is at `+0x4`. Seven types are
never used in stock XML: ExpandableMenuDef, StaticFileImageDef,
ImagePickerDef, FrameAnimDef, StagedStatusBarDef, IconGroupDef,
RenderAreaDef.

Special cases worth knowing before you edit:

- **IconDef `HideLabel`** is an int. The stored byte is `value == 0`
  ("show label"), so `<HideLabel>1</HideLabel>` hides the label (88 stock
  uses) and `<HideLabel>true</HideLabel>` shows it (`0x4EF0D8`).
- **IconDef** has no `Width`, `Height` or `OnRightClickEvent` compare. The
  `OnRightClickEvent` keyword string exists (`0x99B90C`), but no code reads
  it. Stock IconDefs with `Width`/`Height` (120 each) are ignored.
- **TabsDef**: `<Tab>` (`0x4ECD60`) reads `<Id>` (int) and `<Name>` (text) and
  appends a tab; `<TabControl>` (`0x4ECCC7`) reads `<TabId>` and `<ControlId>`,
  **both plain `atol`** (no ControlId names), and maps the control to the tab.
- **ScalarLabelDef `Format`** is stored raw; the default is `%d`
  (`0x4EC5F5`). Its consumer is not located.
- **TextAreaDef `HotspotDelineator`** stores only the first character
  (default `[`).
- **StagedStatusBarDef `Zeroframefirst`** compares with `_stricmp(text, "true")`.
- Stock children with no compare (silently ignored): IconDef `Width`/`Height`,
  HorizontalResizeImageDef `Height`, ListBoxDef `IconTemplate`/`Adapter`,
  CheckBoxDef `Font`, TextAreaDef `AdvancedStyle`, InvisibleButtonDef `Label`
  (69 stock uses, mostly empty), and a `LabelDef` nested inside a
  `FullResizeImageDef` (`lfg_window.xml`).

### ButtonDef

Parser `0x4ed73d`. Object `new(0xb0)`, constructor `0x4ec1b3` (base `0x4ec177`). 11 of 13 child elements appear in stock XML.

| Child element | Value type (mapper) | Stored at | Default from constructor | Stock XML | Check |
|---|---|---|---|---|---|
| `Controlid` | ControlId: name or leading-digit number (`0x4e99e6`) | +0x8 | -1 (base) | confirmed (304) | `0x4ed773` |
| `Position` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0xc | 0 (base) | confirmed (359) | `0x4ed7a6` |
| `Alignment` | alignment flags (child names) (`0x4ecbf6`) | +0x14 | 0 (base) | confirmed (79) | `0x4ed7cc` |
| `Labelalignment` | alignment flags (child names) (`0x4ecbf6`) | +0x68 | -1 | confirmed (5) | `0x4ed7ef` |
| `LabelIndent` | int (atol) (`0x7720c0`) | +0x6c | 0 | decompiler only | `0x4ed812` |
| `Templatename` | string, max 31 chars (`0x4ec159`) | +0x20 | empty | confirmed (361) | `0x4ed83c` |
| `Label` | text, max 31 chars, UTF-8 to ANSI (0x4eca97) (`0x4eca97 + 0x4ec159`) | +0x40 | empty | confirmed (261) | `0x4ed85e` |
| `Onclickevent` | **event**: stock name or leading-digit number (`0x4ea06f`) | +0x60 | -1 | confirmed (225) | `0x4ed887` |
| `Onshiftclickevent` | **event**: stock name or leading-digit number (`0x4ea06f`) | +0x64 | not set | confirmed (4) | `0x4ed8b0` |
| `Adapter` | string, max 31 chars (`0x4ec159`) | +0x70 | empty | confirmed (2) | `0x4ed8d9` |
| `Coloradapter` | string, max 31 chars (`0x4ec159`) | +0x90 | empty | confirmed (26) | `0x4ed8f8` |
| `ToolTipID` | int (atol) (`0x7720c0`) | +0x18 | 0 (base) | confirmed (3) | `0x4ed931` |
| `Priority` | int (atol) (`0x7720c0`) | +0x1c | 0 (base) | decompiler only | `0x4ed958` |

### InvisibleButtonDef

Parser `0x4ed9b4`. Object `new(0x30)`, constructor `0x4ec1e6` (base `0x4ec177`). 6 of 9 child elements appear in stock XML.

| Child element | Value type (mapper) | Stored at | Default from constructor | Stock XML | Check |
|---|---|---|---|---|---|
| `Controlid` | ControlId: name or leading-digit number (`0x4e99e6`) | +0x8 | -1 (base) | confirmed (126) | `0x4ed9e7` |
| `Position` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0xc | 0 (base) | confirmed (190) | `0x4eda1a` |
| `Alignment` | alignment flags (child names) (`0x4ecbf6`) | +0x14 | 0 (base) | confirmed (12) | `0x4eda40` |
| `Onclickevent` | **event**: stock name or leading-digit number (`0x4ea06f`) | +0x20 | -1 | confirmed (157) | `0x4eda63` |
| `Onshiftclickevent` | **event**: stock name or leading-digit number (`0x4ea06f`) | +0x24 | not set | decompiler only | `0x4eda89` |
| `Width` | int (atol) (`0x7720c0`) | +0x28 | not set | confirmed (190) | `0x4edab2` |
| `Height` | int (atol) (`0x7720c0`) | +0x2c | not set | confirmed (190) | `0x4edad9` |
| `ToolTipID` | int (atol) (`0x7720c0`) | +0x18 | 0 (base) | decompiler only | `0x4edb00` |
| `Priority` | int (atol) (`0x7720c0`) | +0x1c | 0 (base) | decompiler only | `0x4edb27` |

### ExpandableMenuDef

Parser `0x4edb83`. Object `new(0x64)`, constructor `0x4ec201` (base `0x4ec177`). 0 of 8 child elements appear in stock XML.

| Child element | Value type (mapper) | Stored at | Default from constructor | Stock XML | Check |
|---|---|---|---|---|---|
| `Controlid` | ControlId: name or leading-digit number (`0x4e99e6`) | +0x8 | -1 (base) | decompiler only | `0x4edbb6` |
| `Position` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0xc | 0 (base) | decompiler only | `0x4edbe9` |
| `Alignment` | alignment flags (child names) (`0x4ecbf6`) | +0x14 | 0 (base) | decompiler only | `0x4edc0f` |
| `Templatename` | string, max 31 chars (`0x4ec159`) | +0x20 | empty | decompiler only | `0x4edc32` |
| `Onchangeevent` | **event**: stock name or leading-digit number (`0x4ea06f`) | +0x40 | -1 | decompiler only | `0x4edc51` |
| `Adaptername` | string, max 31 chars (`0x4ec159`) | +0x44 | empty | decompiler only | `0x4edc7a` |
| `ToolTipID` | int (atol) (`0x7720c0`) | +0x18 | 0 (base) | decompiler only | `0x4edcb0` |
| `Priority` | int (atol) (`0x7720c0`) | +0x1c | 0 (base) | decompiler only | `0x4edcd7` |

### ComboBoxDef

Parser `0x4edd33`. Object `new(0x64)`, constructor `0x4ec224` (base `0x4ec177`). 4 of 8 child elements appear in stock XML.

| Child element | Value type (mapper) | Stored at | Default from constructor | Stock XML | Check |
|---|---|---|---|---|---|
| `Controlid` | ControlId: name or leading-digit number (`0x4e99e6`) | +0x8 | -1 (base) | confirmed (53) | `0x4edd66` |
| `Position` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0xc | 0 (base) | confirmed (53) | `0x4edd99` |
| `Alignment` | alignment flags (child names) (`0x4ecbf6`) | +0x14 | 0 (base) | decompiler only | `0x4eddbf` |
| `Templatename` | string, max 31 chars (`0x4ec159`) | +0x20 | empty | confirmed (53) | `0x4edde2` |
| `Onchangeevent` | **event**: stock name or leading-digit number (`0x4ea06f`) | +0x40 | -1 | decompiler only | `0x4ede01` |
| `Adaptername` | string, max 31 chars (`0x4ec159`) | +0x44 | empty | decompiler only | `0x4ede2a` |
| `ToolTipID` | int (atol) (`0x7720c0`) | +0x18 | 0 (base) | decompiler only | `0x4ede60` |
| `Priority` | int (atol) (`0x7720c0`) | +0x1c | 0 (base) | confirmed (4) | `0x4ede87` |

### ListBoxDef

Parser `0x4edee3`. Object `new(0xa4)`, constructor `0x4ec247` (base `0x4ec177`). 14 of 18 child elements appear in stock XML.

| Child element | Value type (mapper) | Stored at | Default from constructor | Stock XML | Check |
|---|---|---|---|---|---|
| `Controlid` | ControlId: name or leading-digit number (`0x4e99e6`) | +0x8 | -1 (base) | confirmed (38) | `0x4edf1b` |
| `Position` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0xc | 0 (base) | confirmed (38) | `0x4edf4e` |
| `Alignment` | alignment flags (child names) (`0x4ecbf6`) | +0x14 | 0 (base) | confirmed (31) | `0x4edf75` |
| `Templatename` | string, max 31 chars (`0x4ec159`) | +0x20 | empty | confirmed (38) | `0x4edf98` |
| `Adaptername` | string, max 31 chars (`0x4ec159`) | +0x40 | empty | confirmed (38) | `0x4edfb7` |
| `Fontname` | string, max 31 chars (`0x4ec159`) | +0x60 | empty | confirmed (5) | `0x4edfd6` |
| `Sorting` | bool: text `true` (any case) = 1 (`0x4f259b`) | +0x80 | empty | confirmed (38) | `0x4ee00c` |
| `Columns` | int (atol) (`0x7720c0`) | +0x84 | 0 | confirmed (20) | `0x4ee02b` |
| `Width` | int (atol) (`0x7720c0`) | +0x88 | not set | confirmed (38) | `0x4ee058` |
| `Height` | int (atol) (`0x7720c0`) | +0x8c | not set | confirmed (38) | `0x4ee085` |
| `RowHeight` | int (atol) (`0x7720c0`) | +0x90 | 0 | decompiler only | `0x4ee0b2` |
| `ToolTipID` | int (atol) (`0x7720c0`) | +0x18 | 10135 (0x2797) | decompiler only | `0x4ee0df` |
| `ShowIndicator` | bool: text `true` (any case) = 1 (`0x4f259b`) | +0x94 | empty | confirmed (8) | `0x4ee109` |
| `ShowDefaultToolTips` | bool: text `true` (any case) = 1 (`0x4f259b`) | +0x95 | empty | confirmed (10) | `0x4ee125` |
| `OnHeaderClickEvent` | **event**: stock name or leading-digit number (`0x4ea06f`) | +0x98 | not set | confirmed (8) | `0x4ee14b` |
| `OnClickEvent` | **event**: stock name or leading-digit number (`0x4ea06f`) | +0x9c | not set | confirmed (8) | `0x4ee177` |
| `OnShiftClickEvent` | **event**: stock name or leading-digit number (`0x4ea06f`) | +0xa0 | not set | decompiler only | `0x4ee1a3` |
| `Priority` | int (atol) (`0x7720c0`) | +0x1c | 0 (base) | decompiler only | `0x4ee1cf` |

### StaticFileImageDef

Parser `0x4ee230`. Object `new(0x40)`, constructor `0x4ec28e` (base `0x4ec177`). 0 of 6 child elements appear in stock XML.

| Child element | Value type (mapper) | Stored at | Default from constructor | Stock XML | Check |
|---|---|---|---|---|---|
| `Controlid` | ControlId: name or leading-digit number (`0x4e99e6`) | +0x8 | -1 (base) | decompiler only | `0x4ee263` |
| `Position` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0xc | 0 (base) | decompiler only | `0x4ee296` |
| `Alignment` | alignment flags (child names) (`0x4ecbf6`) | +0x14 | 0 (base) | decompiler only | `0x4ee2bc` |
| `Canvasname` | string, max 31 chars (`0x4ec159`) | +0x20 | empty | decompiler only | `0x4ee2df` |
| `ToolTipID` | int (atol) (`0x7720c0`) | +0x18 | 0 (base) | decompiler only | `0x4ee315` |
| `Priority` | int (atol) (`0x7720c0`) | +0x1c | 0 (base) | decompiler only | `0x4ee33c` |

### StatusBarDef

Parser `0x4ee398`. Object `new(0x64)`, constructor `0x4ec2a9` (base `0x4ec177`). 7 of 8 child elements appear in stock XML.

| Child element | Value type (mapper) | Stored at | Default from constructor | Stock XML | Check |
|---|---|---|---|---|---|
| `Controlid` | ControlId: name or leading-digit number (`0x4e99e6`) | +0x8 | -1 (base) | confirmed (32) | `0x4ee3cb` |
| `Position` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0xc | 0 (base) | confirmed (111) | `0x4ee3fe` |
| `Alignment` | alignment flags (child names) (`0x4ecbf6`) | +0x14 | 0 (base) | confirmed (17) | `0x4ee424` |
| `Templatename` | string, max 31 chars (`0x4ec159`) | +0x20 | empty | confirmed (111) | `0x4ee447` |
| `Adaptername` | string, max 31 chars (`0x4ec159`) | +0x40 | empty | confirmed (111) | `0x4ee466` |
| `Width` | int (atol) (`0x7720c0`) | +0x60 | not set | confirmed (111) | `0x4ee49c` |
| `ToolTipID` | int (atol) (`0x7720c0`) | +0x18 | 0 (base) | confirmed (7) | `0x4ee4c3` |
| `Priority` | int (atol) (`0x7720c0`) | +0x1c | 0 (base) | decompiler only | `0x4ee4ea` |

### LabelDef

Parser `0x4ee546`. Object `new(0xbc)`, constructor `0x4ec2c8` (base `0x4ec177`). 14 of 15 child elements appear in stock XML.

| Child element | Value type (mapper) | Stored at | Default from constructor | Stock XML | Check |
|---|---|---|---|---|---|
| `Controlid` | ControlId: name or leading-digit number (`0x4e99e6`) | +0x8 | -1 (base) | confirmed (246) | `0x4ee57e` |
| `Position` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0xc | 0 (base) | confirmed (878) | `0x4ee5b1` |
| `Alignment` | alignment flags (child names) (`0x4ecbf6`) | +0x14 | 0 (base) | confirmed (178) | `0x4ee5d8` |
| `Fontname` | string, max 31 chars (`0x4ec159`) | +0x20 | empty | confirmed (878) | `0x4ee5fb` |
| `Adapter` | string, max 31 chars (`0x4ec159`) | +0x40 | empty | confirmed (432) | `0x4ee61a` |
| `Coloradapter` | string, max 31 chars (`0x4ec159`) | +0x60 | empty | confirmed (223) | `0x4ee639` |
| `Width` | int (atol) (`0x7720c0`) | +0x80 | 0 | confirmed (878) | `0x4ee66f` |
| `Height` | int (atol) (`0x7720c0`) | +0x84 | 0 | confirmed (878) | `0x4ee69c` |
| `Maxcharacters` | int (atol) (`0x7720c0`) | +0x88 | 0 | confirmed (878) | `0x4ee6c9` |
| `Color` | color: `<R><G><B><A>` 0-255, stored /255 (`0x4f239c`) | +0xa8 | 0,0,0,0 (fldz, 0x4ec2ec) | confirmed (852) | `0x4ee6f6` |
| `Data` | text, unbounded, UTF-8 to ANSI (0x4eca97) (`0x4eca97 + 0x404d9a`) | +0x8c | empty | confirmed (776) | `0x4ee735` |
| `Endaligned` | bool: text `true` (any case) = 1 (`0x4f259b`) | +0xb8 | empty | confirmed (28) | `0x4ee761` |
| `Textcentered` | bool: text `true` (any case) = 1 (`0x4f259b`) | +0xb9 | empty | confirmed (26) | `0x4ee77d` |
| `ToolTipID` | int (atol) (`0x7720c0`) | +0x18 | 0 (base) | confirmed (18) | `0x4ee7a3` |
| `Priority` | int (atol) (`0x7720c0`) | +0x1c | 0 (base) | decompiler only | `0x4ee7ca` |

### FullResizeImageDef

Parser `0x4ee82b`. Object `new(0x48)`, constructor `0x4ec360` (base `0x4ec177`). 6 of 8 child elements appear in stock XML.

| Child element | Value type (mapper) | Stored at | Default from constructor | Stock XML | Check |
|---|---|---|---|---|---|
| `Controlid` | ControlId: name or leading-digit number (`0x4e99e6`) | +0x8 | -1 (base) | confirmed (105) | `0x4ee85e` |
| `Position` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0xc | 0 (base) | confirmed (169) | `0x4ee891` |
| `Alignment` | alignment flags (child names) (`0x4ecbf6`) | +0x14 | 0 (base) | confirmed (116) | `0x4ee8b7` |
| `Templatename` | string, max 31 chars (`0x4ec159`) | +0x20 | empty | confirmed (168) | `0x4ee8da` |
| `Width` | int (atol) (`0x7720c0`) | +0x40 | not set | confirmed (169) | `0x4ee910` |
| `Height` | int (atol) (`0x7720c0`) | +0x44 | not set | confirmed (170) | `0x4ee937` |
| `ToolTipID` | int (atol) (`0x7720c0`) | +0x18 | 0 (base) | decompiler only | `0x4ee95e` |
| `Priority` | int (atol) (`0x7720c0`) | +0x1c | 0 (base) | decompiler only | `0x4ee985` |

### TextAreaDef

Parser `0x4ee9e1`. Object `new(0x70)`, constructor `0x4ec37b` (base `0x4ec177`). 10 of 12 child elements appear in stock XML.

| Child element | Value type (mapper) | Stored at | Default from constructor | Stock XML | Check |
|---|---|---|---|---|---|
| `Controlid` | ControlId: name or leading-digit number (`0x4e99e6`) | +0x8 | -1 (base) | confirmed (30) | `0x4eea14` |
| `Position` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0xc | 0 (base) | confirmed (30) | `0x4eea47` |
| `Alignment` | alignment flags (child names) (`0x4ecbf6`) | +0x14 | 0 (base) | confirmed (24) | `0x4eea6d` |
| `Templatename` | string, max 31 chars (`0x4ec159`) | +0x20 | empty | confirmed (30) | `0x4eea90` |
| `Adaptername` | string, max 31 chars (`0x4ec159`) | +0x40 | empty | confirmed (30) | `0x4eeaaf` |
| `Hashotspots` | bool: text `true` (any case) = 1 (`0x4f259b`) | +0x60 | not set | confirmed (28) | `0x4eeae5` |
| `HotspotDelineator` | single character (first byte of the text) (`inline`) | byte +0x61 | `[` (0x5b) | confirmed (2) | `0x4eeb08` |
| `Maxcharacters` | int (atol) (`0x7720c0`) | +0x64 | not set | confirmed (30) | `0x4eeb2d` |
| `Width` | int (atol) (`0x7720c0`) | +0x68 | not set | confirmed (30) | `0x4eeb57` |
| `Height` | int (atol) (`0x7720c0`) | +0x6c | not set | confirmed (30) | `0x4eeb7e` |
| `ToolTipID` | int (atol) (`0x7720c0`) | +0x18 | 0 (base) | decompiler only | `0x4eeba5` |
| `Priority` | int (atol) (`0x7720c0`) | +0x1c | 0 (base) | decompiler only | `0x4eebcc` |

### ImageAreaDef

Parser `0x4eec28`. Object `new(0x40)`, constructor `0x4ec39e` (base `0x4ec177`). 4 of 6 child elements appear in stock XML.

| Child element | Value type (mapper) | Stored at | Default from constructor | Stock XML | Check |
|---|---|---|---|---|---|
| `Controlid` | ControlId: name or leading-digit number (`0x4e99e6`) | +0x8 | -1 (base) | confirmed (366) | `0x4eec5b` |
| `Position` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0xc | 0 (base) | confirmed (556) | `0x4eec8e` |
| `Alignment` | alignment flags (child names) (`0x4ecbf6`) | +0x14 | 0 (base) | confirmed (20) | `0x4eecb4` |
| `Templatename` | string, max 31 chars (`0x4ec159`) | +0x20 | empty | confirmed (556) | `0x4eecd7` |
| `ToolTipID` | int (atol) (`0x7720c0`) | +0x18 | 0 (base) | decompiler only | `0x4eed0d` |
| `Priority` | int (atol) (`0x7720c0`) | +0x1c | 0 (base) | decompiler only | `0x4eed34` |

### StatusIconDef

Parser `0x4eed90`. Object `new(0x60)`, constructor `0x4ec3b9` (base `0x4ec177`). 5 of 7 child elements appear in stock XML.

| Child element | Value type (mapper) | Stored at | Default from constructor | Stock XML | Check |
|---|---|---|---|---|---|
| `Controlid` | ControlId: name or leading-digit number (`0x4e99e6`) | +0x8 | -1 (base) | confirmed (435) | `0x4eedc3` |
| `Position` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0xc | 0 (base) | confirmed (467) | `0x4eedf6` |
| `Alignment` | alignment flags (child names) (`0x4ecbf6`) | +0x14 | 0 (base) | confirmed (3) | `0x4eee1c` |
| `Templatename` | string, max 31 chars (`0x4ec159`) | +0x20 | empty | confirmed (467) | `0x4eee3f` |
| `Adaptername` | string, max 31 chars (`0x4ec159`) | +0x40 | empty | confirmed (467) | `0x4eee5e` |
| `ToolTipID` | int (atol) (`0x7720c0`) | +0x18 | 0 (base) | decompiler only | `0x4eee94` |
| `Priority` | int (atol) (`0x7720c0`) | +0x1c | 0 (base) | decompiler only | `0x4eeebb` |

### IconDef

Parser `0x4eef17`. Object `new(0xc8)`, constructor `0x4ec3d8` (base `0x4ec177`). 10 of 13 child elements appear in stock XML.

| Child element | Value type (mapper) | Stored at | Default from constructor | Stock XML | Check |
|---|---|---|---|---|---|
| `Controlid` | ControlId: name or leading-digit number (`0x4e99e6`) | +0x8 | -1 (base) | confirmed (136) | `0x4eef4d` |
| `Position` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0xc | 0 (base) | confirmed (244) | `0x4eef80` |
| `Alignment` | alignment flags (child names) (`0x4ecbf6`) | +0x14 | 0 (base) | decompiler only | `0x4eefa6` |
| `Iconid` | int (atol) (`0x7720c0`) | +0xc0 | not set | confirmed (232) | `0x4eefc9` |
| `Data` | string, unbounded (std::string) (`0x404d9a`) | +0xa0 | empty | decompiler only | `0x4eeff6` |
| `Templatename` | string, max 31 chars (`0x4ec159`) | +0x20 | empty | confirmed (244) | `0x4ef025` |
| `Adaptername` | string, max 31 chars (`0x4ec159`) | +0x40 | empty | confirmed (236) | `0x4ef044` |
| `LabelAdapterName` | string, max 31 chars (`0x4ec159`) | +0x60 | empty | confirmed (120) | `0x4ef063` |
| `StateAdapterName` | string, max 31 chars (`0x4ec159`) | +0x80 | empty | confirmed (24) | `0x4ef082` |
| `HideLabel` | int; stores `value == 0` as "show label": `1` hides, `0` or `true` shows (`0x7720c0`) | byte +0xc4 | 1 | confirmed (120) | `0x4ef0bb` |
| `ToolTipID` | int (atol) (`0x7720c0`) | +0x18 | 0 (base) | confirmed (120) | `0x4ef0eb` |
| `OnClickEvent` | **event**: stock name or leading-digit number (`0x4ea06f`) | +0xbc | -1 | confirmed (16) | `0x4ef112` |
| `Priority` | int (atol) (`0x7720c0`) | +0x1c | 0 (base) | decompiler only | `0x4ef13e` |

### ChatControlDef

Parser `0x4ef19a`. Object `new(0x74)`, constructor `0x4ec44b` (base `0x4ec177`). 9 of 11 child elements appear in stock XML.

| Child element | Value type (mapper) | Stored at | Default from constructor | Stock XML | Check |
|---|---|---|---|---|---|
| `Controlid` | ControlId: name or leading-digit number (`0x4e99e6`) | +0x8 | -1 (base) | confirmed (3) | `0x4ef1cd` |
| `Position` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0xc | 0 (base) | confirmed (3) | `0x4ef200` |
| `Alignment` | alignment flags (child names) (`0x4ecbf6`) | +0x14 | 0 (base) | confirmed (3) | `0x4ef226` |
| `Templatename` | string, max 31 chars (`0x4ec159`) | +0x20 | empty | confirmed (3) | `0x4ef249` |
| `Linepadding` | int (atol) (`0x7720c0`) | +0x40 | not set | confirmed (3) | `0x4ef268` |
| `Buffername` | string, max 31 chars (`0x4ec159`) | +0x4c | empty | confirmed (3) | `0x4ef292` |
| `Textoffset` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0x44 | 0 | confirmed (3) | `0x4ef2c8` |
| `Width` | int (atol) (`0x7720c0`) | +0x6c | not set | confirmed (3) | `0x4ef2e4` |
| `Height` | int (atol) (`0x7720c0`) | +0x70 | not set | confirmed (3) | `0x4ef30b` |
| `ToolTipID` | int (atol) (`0x7720c0`) | +0x18 | 0 (base) | decompiler only | `0x4ef332` |
| `Priority` | int (atol) (`0x7720c0`) | +0x1c | 0 (base) | decompiler only | `0x4ef359` |

### HorizontalResizeImageDef

Parser `0x4ef3b5`. Object `new(0x44)`, constructor `0x4ec470` (base `0x4ec177`). 6 of 7 child elements appear in stock XML.

| Child element | Value type (mapper) | Stored at | Default from constructor | Stock XML | Check |
|---|---|---|---|---|---|
| `Controlid` | ControlId: name or leading-digit number (`0x4e99e6`) | +0x8 | -1 (base) | confirmed (44) | `0x4ef3e8` |
| `Position` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0xc | 0 (base) | confirmed (206) | `0x4ef41b` |
| `Alignment` | alignment flags (child names) (`0x4ecbf6`) | +0x14 | 0 (base) | confirmed (4) | `0x4ef441` |
| `Templatename` | string, max 31 chars (`0x4ec159`) | +0x20 | empty | confirmed (204) | `0x4ef464` |
| `Width` | int (atol) (`0x7720c0`) | +0x40 | not set | confirmed (206) | `0x4ef49a` |
| `ToolTipID` | int (atol) (`0x7720c0`) | +0x18 | 0 (base) | confirmed (34) | `0x4ef4c1` |
| `Priority` | int (atol) (`0x7720c0`) | +0x1c | 0 (base) | decompiler only | `0x4ef4e8` |

### VerticalResizeImageDef

Parser `0x4ef544`. Object `new(0x44)`, constructor `0x4ec48b` (base `0x4ec177`). 4 of 7 child elements appear in stock XML.

| Child element | Value type (mapper) | Stored at | Default from constructor | Stock XML | Check |
|---|---|---|---|---|---|
| `Controlid` | ControlId: name or leading-digit number (`0x4e99e6`) | +0x8 | -1 (base) | decompiler only | `0x4ef577` |
| `Position` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0xc | 0 (base) | confirmed (3) | `0x4ef5aa` |
| `Alignment` | alignment flags (child names) (`0x4ecbf6`) | +0x14 | 0 (base) | confirmed (1) | `0x4ef5d0` |
| `Templatename` | string, max 31 chars (`0x4ec159`) | +0x20 | empty | confirmed (3) | `0x4ef5f3` |
| `Height` | int (atol) (`0x7720c0`) | +0x40 | not set | confirmed (3) | `0x4ef629` |
| `ToolTipID` | int (atol) (`0x7720c0`) | +0x18 | 0 (base) | decompiler only | `0x4ef650` |
| `Priority` | int (atol) (`0x7720c0`) | +0x1c | 0 (base) | decompiler only | `0x4ef677` |

### CheckBoxDef

Parser `0x4ef6d3`. Object `new(0xa8)`, constructor `0x4ec4ad` (base `0x4ec177`). 10 of 12 child elements appear in stock XML.

| Child element | Value type (mapper) | Stored at | Default from constructor | Stock XML | Check |
|---|---|---|---|---|---|
| `Controlid` | ControlId: name or leading-digit number (`0x4e99e6`) | +0x8 | -1 (base) | confirmed (321) | `0x4ef709` |
| `Position` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0xc | 0 (base) | confirmed (321) | `0x4ef73c` |
| `Alignment` | alignment flags (child names) (`0x4ecbf6`) | +0x14 | 0 (base) | confirmed (9) | `0x4ef762` |
| `Templatename` | string, max 31 chars (`0x4ec159`) | +0x20 | empty | confirmed (321) | `0x4ef785` |
| `Adaptername` | string, max 31 chars (`0x4ec159`) | +0x40 | empty | confirmed (65) | `0x4ef7a4` |
| `Textadaptername` | string, max 31 chars (`0x4ec159`) | +0x60 | empty | confirmed (39) | `0x4ef7c3` |
| `Data` | text, unbounded, UTF-8 to ANSI (0x4eca97) (`0x4eca97 + 0x404d9a`) | +0x80 | empty | confirmed (252) | `0x4ef7f9` |
| `Labelwidth` | int (atol) (`0x7720c0`) | +0x9c | 0 | confirmed (278) | `0x4ef825` |
| `ToolTipID` | int (atol) (`0x7720c0`) | +0x18 | 0 (base) | confirmed (12) | `0x4ef852` |
| `OnClickEvent` | **event**: stock name or leading-digit number (`0x4ea06f`) | +0xa0 | not set | confirmed (22) | `0x4ef879` |
| `OnShiftClickEvent` | **event**: stock name or leading-digit number (`0x4ea06f`) | +0xa4 | not set | decompiler only | `0x4ef8a5` |
| `Priority` | int (atol) (`0x7720c0`) | +0x1c | 0 (base) | decompiler only | `0x4ef8d1` |

### VerticalStatusBarDef

Parser `0x4ef92d`. Object `new(0x68)`, constructor `0x4ec551` (base `0x4ec177`). 6 of 10 child elements appear in stock XML.

| Child element | Value type (mapper) | Stored at | Default from constructor | Stock XML | Check |
|---|---|---|---|---|---|
| `Controlid` | ControlId: name or leading-digit number (`0x4e99e6`) | +0x8 | -1 (base) | confirmed (4) | `0x4ef960` |
| `Position` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0xc | 0 (base) | confirmed (4) | `0x4ef993` |
| `Alignment` | alignment flags (child names) (`0x4ecbf6`) | +0x14 | 0 (base) | confirmed (4) | `0x4ef9b9` |
| `Templatename` | string, max 31 chars (`0x4ec159`) | +0x20 | empty | confirmed (4) | `0x4ef9dc` |
| `Adaptername` | string, max 31 chars (`0x4ec159`) | +0x40 | empty | confirmed (4) | `0x4ef9fb` |
| `Height` | int (atol) (`0x7720c0`) | +0x60 | not set | confirmed (4) | `0x4efa31` |
| `Dontdrawwhenzero` | bool: text `true` (any case) = 1 (`0x4f259b`) | +0x64 | empty | decompiler only | `0x4efa5b` |
| `Dontdrawwhennegone` | bool: text `true` (any case) = 1 (`0x4f259b`) | +0x65 | empty | decompiler only | `0x4efa74` |
| `ToolTipID` | int (atol) (`0x7720c0`) | +0x18 | 0 (base) | decompiler only | `0x4efa97` |
| `Priority` | int (atol) (`0x7720c0`) | +0x1c | 0 (base) | decompiler only | `0x4efabe` |

### CompassControlDef

Parser `0x4efb1a`. Object `new(0x68)`, constructor `0x4ec576` (base `0x4ec177`). 6 of 9 child elements appear in stock XML.

| Child element | Value type (mapper) | Stored at | Default from constructor | Stock XML | Check |
|---|---|---|---|---|---|
| `Controlid` | ControlId: name or leading-digit number (`0x4e99e6`) | +0x8 | -1 (base) | confirmed (4) | `0x4efb4d` |
| `Position` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0xc | 0 (base) | confirmed (4) | `0x4efb80` |
| `Alignment` | alignment flags (child names) (`0x4ecbf6`) | +0x14 | 0 (base) | decompiler only | `0x4efba6` |
| `Templatename` | string, max 31 chars (`0x4ec159`) | +0x20 | empty | confirmed (4) | `0x4efbc9` |
| `Adaptername` | string, max 31 chars (`0x4ec159`) | +0x40 | empty | confirmed (4) | `0x4efbe8` |
| `Height` | int (atol) (`0x7720c0`) | +0x64 | not set | confirmed (4) | `0x4efc1e` |
| `Width` | int (atol) (`0x7720c0`) | +0x60 | not set | confirmed (4) | `0x4efc45` |
| `ToolTipID` | int (atol) (`0x7720c0`) | +0x18 | 0 (base) | decompiler only | `0x4efc6c` |
| `Priority` | int (atol) (`0x7720c0`) | +0x1c | 0 (base) | decompiler only | `0x4efc93` |

### ScalarLabelDef

Parser `0x4efcef`. Object `new(0xd8)`, constructor `0x4ec595` (base `0x4ec177`). 16 of 18 child elements appear in stock XML.

| Child element | Value type (mapper) | Stored at | Default from constructor | Stock XML | Check |
|---|---|---|---|---|---|
| `Controlid` | ControlId: name or leading-digit number (`0x4e99e6`) | +0x8 | -1 (base) | confirmed (109) | `0x4efd39` |
| `Position` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0xc | 0 (base) | confirmed (178) | `0x4efd6a` |
| `Alignment` | alignment flags (child names) (`0x4ecbf6`) | +0x14 | 0 (base) | confirmed (17) | `0x4efd91` |
| `Fontname` | string, max 31 chars (`0x4ec159`) | +0x20 | empty | confirmed (178) | `0x4efdb4` |
| `Adapter` | string, max 31 chars (`0x4ec159`) | +0x40 | empty | confirmed (178) | `0x4efdd3` |
| `Coloradapter` | string, max 31 chars (`0x4ec159`) | +0x60 | empty | confirmed (62) | `0x4efdf2` |
| `Width` | int (atol) (`0x7720c0`) | +0x80 | 0 | confirmed (178) | `0x4efe21` |
| `Height` | int (atol) (`0x7720c0`) | +0x84 | 0 | confirmed (178) | `0x4efe59` |
| `Maxcharacters` | int (atol) (`0x7720c0`) | +0x88 | 0 | confirmed (178) | `0x4efe83` |
| `Color` | color: `<R><G><B><A>` 0-255, stored /255 (`0x4f239c`) | +0xa8 | 0,0,0,0 (fldz, 0x4ec5cb) | confirmed (178) | `0x4efead` |
| `Data` | text, unbounded, UTF-8 to ANSI (0x4eca97) (`0x4eca97 + 0x404d9a`) | +0x8c | empty | confirmed (45) | `0x4efeec` |
| `Endaligned` | bool: text `true` (any case) = 1 (`0x4f259b`) | +0xb8 | empty | confirmed (19) | `0x4eff18` |
| `Textcentered` | bool: text `true` (any case) = 1 (`0x4f259b`) | +0xbb | empty | confirmed (31) | `0x4eff34` |
| `Dontdrawwhenzero` | bool: text `true` (any case) = 1 (`0x4f259b`) | +0xb9 | empty | confirmed (35) | `0x4eff50` |
| `Dontdrawwhennegone` | bool: text `true` (any case) = 1 (`0x4f259b`) | +0xba | empty | confirmed (7) | `0x4eff6c` |
| `ToolTipID` | int (atol) (`0x7720c0`) | +0x18 | 0 (base) | confirmed (2) | `0x4eff93` |
| `Priority` | int (atol) (`0x7720c0`) | +0x1c | 0 (base) | decompiler only | `0x4effbd` |
| `Format` | string, unbounded (std::string) (`0x404d9a`) | +0xbc | `%d` (0x4ec5f5) | decompiler only | `0x4effe7` |

### TreeControlDef

Parser `0x4f0054`. Object `new(0x78)`, constructor `0x4ec686` (base `0x4ec177`). 11 of 12 child elements appear in stock XML.

| Child element | Value type (mapper) | Stored at | Default from constructor | Stock XML | Check |
|---|---|---|---|---|---|
| `Controlid` | ControlId: name or leading-digit number (`0x4e99e6`) | +0x8 | -1 (base) | confirmed (6) | `0x4f0087` |
| `Position` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0xc | 0 (base) | confirmed (6) | `0x4f00ba` |
| `Alignment` | alignment flags (child names) (`0x4ecbf6`) | +0x14 | 0 (base) | confirmed (4) | `0x4f00e0` |
| `Templatename` | string, max 31 chars (`0x4ec159`) | +0x20 | empty | confirmed (6) | `0x4f0103` |
| `Adaptername` | string, max 31 chars (`0x4ec159`) | +0x40 | empty | confirmed (6) | `0x4f0122` |
| `Width` | int (atol) (`0x7720c0`) | +0x60 | 0 | confirmed (6) | `0x4f0158` |
| `Height` | int (atol) (`0x7720c0`) | +0x64 | 0 | confirmed (6) | `0x4f0182` |
| `Column1width` | int (atol) (`0x7720c0`) | +0x68 | 0 | confirmed (6) | `0x4f01ac` |
| `Column2width` | int (atol) (`0x7720c0`) | +0x6c | 0 | confirmed (6) | `0x4f01d6` |
| `Column1offset` | int (atol) (`0x7720c0`) | +0x70 | 0 | confirmed (6) | `0x4f01fd` |
| `Column2offset` | int (atol) (`0x7720c0`) | +0x74 | 0 | confirmed (6) | `0x4f0224` |
| `Priority` | int (atol) (`0x7720c0`) | +0x1c | 0 (base) | decompiler only | `0x4f024b` |

### IconSetDef

Parser `0x4f02a7`. Object `new(0x68)`, constructor `0x4ec6b7` (base `0x4ec177`). 5 of 8 child elements appear in stock XML.

| Child element | Value type (mapper) | Stored at | Default from constructor | Stock XML | Check |
|---|---|---|---|---|---|
| `Controlid` | ControlId: name or leading-digit number (`0x4e99e6`) | +0x8 | -1 (base) | decompiler only | `0x4f02da` |
| `Position` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0xc | 0 (base) | confirmed (16) | `0x4f030d` |
| `Alignment` | alignment flags (child names) (`0x4ecbf6`) | +0x14 | 0 (base) | decompiler only | `0x4f0333` |
| `Templatename` | string, max 31 chars (`0x4ec159`) | +0x20 | empty | confirmed (16) | `0x4f0356` |
| `Baseadaptername` | string, max 31 chars (`0x4ec159`) | +0x40 | empty | confirmed (16) | `0x4f0375` |
| `Rows` | int (atol) (`0x7720c0`) | +0x60 | 0 | confirmed (16) | `0x4f03ab` |
| `Columns` | int (atol) (`0x7720c0`) | +0x64 | 0 | confirmed (16) | `0x4f03d2` |
| `Priority` | int (atol) (`0x7720c0`) | +0x1c | 0 (base) | decompiler only | `0x4f03f9` |

### DockableIconDef

Parser `0x4f0455`. Object `new(0x44)`, constructor `0x4ec6dc` (base `0x4ec177`). 3 of 7 child elements appear in stock XML.

| Child element | Value type (mapper) | Stored at | Default from constructor | Stock XML | Check |
|---|---|---|---|---|---|
| `Controlid` | ControlId: name or leading-digit number (`0x4e99e6`) | +0x8 | -1 (base) | confirmed (14) | `0x4f0488` |
| `Position` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0xc | 0 (base) | confirmed (14) | `0x4f04bb` |
| `Alignment` | alignment flags (child names) (`0x4ecbf6`) | +0x14 | 0 (base) | decompiler only | `0x4f04e1` |
| `Icontemplatename` | string, max 31 chars (`0x4ec159`) | +0x20 | empty | confirmed (14) | `0x4f0504` |
| `ToolTipID` | int (atol) (`0x7720c0`) | +0x18 | 0 (base) | decompiler only | `0x4f053a` |
| `OnClickEvent` | **event**: stock name or leading-digit number (`0x4ea06f`) | +0x40 | not set | decompiler only | `0x4f0561` |
| `Priority` | int (atol) (`0x7720c0`) | +0x1c | 0 (base) | decompiler only | `0x4f058a` |

### DynamicImageDef

Parser `0x4f05e6`. Object `new(0x4c)`, constructor `0x4ec854` (base `0x4ec177`). 5 of 8 child elements appear in stock XML.

| Child element | Value type (mapper) | Stored at | Default from constructor | Stock XML | Check |
|---|---|---|---|---|---|
| `Controlid` | ControlId: name or leading-digit number (`0x4e99e6`) | +0x8 | -1 (base) | confirmed (4) | `0x4f0619` |
| `Position` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0xc | 0 (base) | confirmed (4) | `0x4f064c` |
| `Alignment` | alignment flags (child names) (`0x4ecbf6`) | +0x14 | 0 (base) | decompiler only | `0x4f0672` |
| `Texturename` | text, unbounded, UTF-8 to ANSI (0x4eca97) (`0x4eca97 + 0x404d9a`) | +0x20 | empty | confirmed (4) | `0x4f0695` |
| `Texturecoords` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0x3c | 0 | confirmed (4) | `0x4f06be` |
| `Dimensions` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0x44 | 0 | confirmed (4) | `0x4f06d7` |
| `ToolTipID` | int (atol) (`0x7720c0`) | +0x18 | 0 (base) | decompiler only | `0x4f06f3` |
| `Priority` | int (atol) (`0x7720c0`) | +0x1c | 0 (base) | decompiler only | `0x4f071a` |

### EditBoxDef

Parser `0x4f0776`. Object `new(0x8c)`, constructor `0x4ec6f7` (base `0x4ec177`). 8 of 11 child elements appear in stock XML.

| Child element | Value type (mapper) | Stored at | Default from constructor | Stock XML | Check |
|---|---|---|---|---|---|
| `Controlid` | ControlId: name or leading-digit number (`0x4e99e6`) | +0x8 | -1 (base) | confirmed (1) | `0x4f07ac` |
| `Position` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0xc | 0 (base) | confirmed (1) | `0x4f07df` |
| `Alignment` | alignment flags (child names) (`0x4ecbf6`) | +0x14 | 0 (base) | confirmed (1) | `0x4f0805` |
| `Templatename` | string, max 31 chars (`0x4ec159`) | +0x20 | empty | confirmed (1) | `0x4f0828` |
| `Adaptername` | string, max 31 chars (`0x4ec159`) | +0x40 | empty | confirmed (1) | `0x4f0847` |
| `Coloradapter` | string, max 31 chars (`0x4ec159`) | +0x60 | empty | decompiler only | `0x4f0866` |
| `Maxcharacters` | int (atol) (`0x7720c0`) | +0x80 | not set | confirmed (1) | `0x4f089c` |
| `Width` | int (atol) (`0x7720c0`) | +0x84 | not set | confirmed (1) | `0x4f08c9` |
| `Height` | int (atol) (`0x7720c0`) | +0x88 | not set | confirmed (1) | `0x4f08f3` |
| `ToolTipID` | int (atol) (`0x7720c0`) | +0x18 | 0 (base) | decompiler only | `0x4f091d` |
| `Priority` | int (atol) (`0x7720c0`) | +0x1c | 0 (base) | decompiler only | `0x4f0944` |

### ClickableEditBoxDef

Parser `0x4f09a0`. Object `new(0x8c)`, constructor `0x4ec719` (base `0x4ec177`). 7 of 11 child elements appear in stock XML.

| Child element | Value type (mapper) | Stored at | Default from constructor | Stock XML | Check |
|---|---|---|---|---|---|
| `Controlid` | ControlId: name or leading-digit number (`0x4e99e6`) | +0x8 | -1 (base) | confirmed (2) | `0x4f09d6` |
| `Position` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0xc | 0 (base) | confirmed (2) | `0x4f0a09` |
| `Alignment` | alignment flags (child names) (`0x4ecbf6`) | +0x14 | 0 (base) | confirmed (1) | `0x4f0a2f` |
| `Templatename` | string, max 31 chars (`0x4ec159`) | +0x20 | empty | confirmed (2) | `0x4f0a52` |
| `Adaptername` | string, max 31 chars (`0x4ec159`) | +0x40 | empty | decompiler only | `0x4f0a71` |
| `Coloradapter` | string, max 31 chars (`0x4ec159`) | +0x60 | empty | decompiler only | `0x4f0a90` |
| `Maxcharacters` | int (atol) (`0x7720c0`) | +0x80 | not set | confirmed (2) | `0x4f0ac6` |
| `Width` | int (atol) (`0x7720c0`) | +0x84 | not set | confirmed (2) | `0x4f0af3` |
| `Height` | int (atol) (`0x7720c0`) | +0x88 | not set | confirmed (2) | `0x4f0b1d` |
| `ToolTipID` | int (atol) (`0x7720c0`) | +0x18 | 0 (base) | decompiler only | `0x4f0b47` |
| `Priority` | int (atol) (`0x7720c0`) | +0x1c | 0 (base) | decompiler only | `0x4f0b6e` |

### HorizontalSliderDef

Parser `0x4f0bca`. Object `new(0x6c)`, constructor `0x4ec73b` (base `0x4ec177`). 6 of 10 child elements appear in stock XML.

| Child element | Value type (mapper) | Stored at | Default from constructor | Stock XML | Check |
|---|---|---|---|---|---|
| `Controlid` | ControlId: name or leading-digit number (`0x4e99e6`) | +0x8 | -1 (base) | confirmed (22) | `0x4f0bfd` |
| `Position` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0xc | 0 (base) | confirmed (22) | `0x4f0c30` |
| `Alignment` | alignment flags (child names) (`0x4ecbf6`) | +0x14 | 0 (base) | decompiler only | `0x4f0c56` |
| `Templatename` | string, max 31 chars (`0x4ec159`) | +0x20 | empty | confirmed (22) | `0x4f0c79` |
| `Adaptername` | string, max 31 chars (`0x4ec159`) | +0x40 | empty | confirmed (22) | `0x4f0c98` |
| `Width` | int (atol) (`0x7720c0`) | +0x60 | not set | confirmed (22) | `0x4f0cce` |
| `Numticks` | int (atol) (`0x7720c0`) | +0x64 | 0 | confirmed (22) | `0x4f0cf8` |
| `ToolTipID` | int (atol) (`0x7720c0`) | +0x18 | 0 (base) | decompiler only | `0x4f0d1f` |
| `OnClickEvent` | **event**: stock name or leading-digit number (`0x4ea06f`) | +0x68 | -1 | decompiler only | `0x4f0d46` |
| `Priority` | int (atol) (`0x7720c0`) | +0x1c | 0 (base) | decompiler only | `0x4f0d6f` |

### VerticalSliderDef

Parser `0x4f0dcb`. Object `new(0x6c)`, constructor `0x4ec761` (base `0x4ec177`). 8 of 10 child elements appear in stock XML.

| Child element | Value type (mapper) | Stored at | Default from constructor | Stock XML | Check |
|---|---|---|---|---|---|
| `Controlid` | ControlId: name or leading-digit number (`0x4e99e6`) | +0x8 | -1 (base) | confirmed (4) | `0x4f0dfe` |
| `Position` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0xc | 0 (base) | confirmed (4) | `0x4f0e31` |
| `Alignment` | alignment flags (child names) (`0x4ecbf6`) | +0x14 | 0 (base) | confirmed (2) | `0x4f0e57` |
| `Templatename` | string, max 31 chars (`0x4ec159`) | +0x20 | empty | confirmed (4) | `0x4f0e7a` |
| `Adaptername` | string, max 31 chars (`0x4ec159`) | +0x40 | empty | confirmed (2) | `0x4f0e99` |
| `Height` | int (atol) (`0x7720c0`) | +0x60 | not set | confirmed (4) | `0x4f0ecf` |
| `Numticks` | int (atol) (`0x7720c0`) | +0x64 | 0 | confirmed (4) | `0x4f0ef9` |
| `ToolTipID` | int (atol) (`0x7720c0`) | +0x18 | 0 (base) | decompiler only | `0x4f0f20` |
| `OnClickEvent` | **event**: stock name or leading-digit number (`0x4ea06f`) | +0x68 | -1 | confirmed (2) | `0x4f0f47` |
| `Priority` | int (atol) (`0x7720c0`) | +0x1c | 0 (base) | decompiler only | `0x4f0f70` |

### ImagePickerDef

Parser `0x4f0fcc`. Object `new(0x60)`, constructor `0x4ec787` (base `0x4ec177`). 0 of 6 child elements appear in stock XML.

| Child element | Value type (mapper) | Stored at | Default from constructor | Stock XML | Check |
|---|---|---|---|---|---|
| `Controlid` | ControlId: name or leading-digit number (`0x4e99e6`) | +0x8 | -1 (base) | decompiler only | `0x4f0fff` |
| `Position` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0xc | 0 (base) | decompiler only | `0x4f1032` |
| `Alignment` | alignment flags (child names) (`0x4ecbf6`) | +0x14 | 0 (base) | decompiler only | `0x4f1058` |
| `Templatename` | string, max 31 chars (`0x4ec159`) | +0x20 | empty | decompiler only | `0x4f107b` |
| `Background` | string, max 31 chars (`0x4ec159`) | +0x40 | empty | decompiler only | `0x4f109a` |
| `Priority` | int (atol) (`0x7720c0`) | +0x1c | 0 (base) | decompiler only | `0x4f10d0` |

### TabsDef

Parser `0x4f112c`. Object `new(0x68)`, constructor `0x4ec7a6` (base `0x4ec177`). 8 of 10 child elements appear in stock XML.

| Child element | Value type (mapper) | Stored at | Default from constructor | Stock XML | Check |
|---|---|---|---|---|---|
| `Controlid` | ControlId: name or leading-digit number (`0x4e99e6`) | +0x8 | -1 (base) | confirmed (3) | `0x4f1173` |
| `TemplateName` | string, max 31 chars (`0x4ec159`) | +0x20 | empty | confirmed (3) | `0x4f11a6` |
| `Position` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0xc | 0 (base) | confirmed (3) | `0x4f11d9` |
| `Width` | int (atol) (`0x7720c0`) | +0x40 | not set | confirmed (3) | `0x4f11ff` |
| `Height` | int (atol) (`0x7720c0`) | +0x44 | not set | confirmed (3) | `0x4f1229` |
| `Tab` | Tab record: `<Id>` int, `<Name>` text (`0x4ecd60`) | +0x48 | empty list (0x5d22f9) | confirmed (13) | `0x4f1253` |
| `TabControl` | TabControl entry: `<TabId>` int, `<ControlId>` int (atol only) (`0x4eccc7`) | +0x54 | empty map (0x4d4d41) | confirmed (343) | `0x4f1276` |
| `OnClickEvent` | **event**: stock name or leading-digit number (`0x4ea06f`) | +0x60 | not set | confirmed (2) | `0x4f129a` |
| `OnShiftClickEvent` | **event**: stock name or leading-digit number (`0x4ea06f`) | +0x64 | not set | decompiler only | `0x4f12c3` |
| `Priority` | int (atol) (`0x7720c0`) | +0x1c | 0 (base) | decompiler only | `0x4f12ec` |

### FrameAnimDef

Parser `0x4f1354`. Object `new(0x50)`, constructor `0x4ec8b0` (base `0x4ec177`). 0 of 9 child elements appear in stock XML.

| Child element | Value type (mapper) | Stored at | Default from constructor | Stock XML | Check |
|---|---|---|---|---|---|
| `Controlid` | ControlId: name or leading-digit number (`0x4e99e6`) | +0x8 | -1 (base) | decompiler only | `0x4f1387` |
| `Position` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0xc | 0 (base) | decompiler only | `0x4f13ba` |
| `Alignment` | alignment flags (child names) (`0x4ecbf6`) | +0x14 | 0 (base) | decompiler only | `0x4f13e0` |
| `Dimensions` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0x3c | 0 | decompiler only | `0x4f1403` |
| `Texturename` | string, unbounded (std::string) (`0x404d9a`) | +0x20 | empty | decompiler only | `0x4f141c` |
| `Maxframes` | int (atol) (`0x7720c0`) | +0x44 | not set | decompiler only | `0x4f144f` |
| `Framedimensions` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0x48 | 0 | decompiler only | `0x4f1476` |
| `ToolTipID` | int (atol) (`0x7720c0`) | +0x18 | 0 (base) | decompiler only | `0x4f1492` |
| `Priority` | int (atol) (`0x7720c0`) | +0x1c | 0 (base) | decompiler only | `0x4f14b9` |

### StagedStatusBarDef

Parser `0x4f1515`. Object `new(0x54)`, constructor `0x4ec8dd` (base `0x4ec177`). 0 of 10 child elements appear in stock XML.

| Child element | Value type (mapper) | Stored at | Default from constructor | Stock XML | Check |
|---|---|---|---|---|---|
| `Controlid` | ControlId: name or leading-digit number (`0x4e99e6`) | +0x8 | -1 (base) | decompiler only | `0x4f1548` |
| `Position` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0xc | 0 (base) | decompiler only | `0x4f157b` |
| `Alignment` | alignment flags (child names) (`0x4ecbf6`) | +0x14 | 0 (base) | decompiler only | `0x4f15a1` |
| `Dimensions` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0x3c | 0 | decompiler only | `0x4f15c4` |
| `Texturename` | string, unbounded (std::string) (`0x404d9a`) | +0x20 | empty | decompiler only | `0x4f15dd` |
| `Maxstates` | int (atol) (`0x7720c0`) | +0x44 | not set | decompiler only | `0x4f1610` |
| `Zeroframefirst` | bool: text `true` (any case) = 1 (_stricmp) (`0x77156e`) | byte +0x48 | not set | decompiler only | `0x4f163a` |
| `Statedimensions` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0x4c | 0 | decompiler only | `0x4f166e` |
| `ToolTipID` | int (atol) (`0x7720c0`) | +0x18 | 0 (base) | decompiler only | `0x4f168a` |
| `Priority` | int (atol) (`0x7720c0`) | +0x1c | 0 (base) | decompiler only | `0x4f16b1` |

### IconGroupDef

Parser `0x4f170d`. Object `new(0x34)`, constructor `0x4ec90a` (base `0x4ec177`). 0 of 6 child elements appear in stock XML.

| Child element | Value type (mapper) | Stored at | Default from constructor | Stock XML | Check |
|---|---|---|---|---|---|
| `ControlId` | ControlId: name or leading-digit number (`0x4e99e6`) | +0x8 | -1 (base) | decompiler only | `0x4f1752` |
| `Position` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0xc | 0 (base) | decompiler only | `0x4f1785` |
| `Alignment` | alignment flags (child names) (`0x4ecbf6`) | +0x14 | 0 (base) | decompiler only | `0x4f17ab` |
| `Dimensions` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0x2c | 0 | decompiler only | `0x4f17ce` |
| `IconSlot` | point `<X>` `<Y>`, appended to a slot list (0x4f1fe8) (`0x4ecb86 + 0x4f1fe8`) | list at +0x20 | empty list (0x53e225) | decompiler only | `0x4f17e7` |
| `Priority` | int (atol) (`0x7720c0`) | +0x1c | 0 (base) | decompiler only | `0x4f181e` |

### RenderAreaDef

Parser `0x4f1886`. Object `new(0x44)`, constructor `0x4ec980` (base `0x4ec177`). 0 of 9 child elements appear in stock XML.

| Child element | Value type (mapper) | Stored at | Default from constructor | Stock XML | Check |
|---|---|---|---|---|---|
| `ControlID` | ControlId: name or leading-digit number (`0x4e99e6`) | +0x8 | -1 (base) | decompiler only | `0x4f18bd` |
| `Position` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0xc | 0 (base) | decompiler only | `0x4f18f0` |
| `Alignment` | alignment flags (child names) (`0x4ecbf6`) | +0x14 | 0 (base) | decompiler only | `0x4f1917` |
| `Dimensions` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0x20 | not set | decompiler only | `0x4f193a` |
| `UseRenderToTexture` | bool: text `true` (any case) = 1 (`0x4f259b`) | +0x28 | not set | decompiler only | `0x4f1953` |
| `UpdateManually` | bool: text `true` (any case) = 1 (`0x4f259b`) | +0x29 | not set | decompiler only | `0x4f196c` |
| `BackgroundColor` | color: `<R><G><B><A>` 0-255, stored /255 (`0x4f239c`) | +0x2c | 0,0,0,0 (fldz, 0x4ec98f) | decompiler only | `0x4f198f` |
| `TextureResolution` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0x3c | not set | decompiler only | `0x4f19bf` |
| `Priority` | int (atol) (`0x7720c0`) | +0x1c | 0 (base) | decompiler only | `0x4f19f5` |

### HorizontalResizeButtonDef

Parser `0x4f1a56`. Object `new(0xb4)`, constructor `0x4ec9f4` (base `0x4ec177`). 10 of 14 child elements appear in stock XML.

| Child element | Value type (mapper) | Stored at | Default from constructor | Stock XML | Check |
|---|---|---|---|---|---|
| `Controlid` | ControlId: name or leading-digit number (`0x4e99e6`) | +0x8 | -1 (base) | confirmed (148) | `0x4f1a8c` |
| `Position` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0xc | 0 (base) | confirmed (148) | `0x4f1abf` |
| `Width` | int (atol) (`0x7720c0`) | +0x64 | 64 | confirmed (148) | `0x4f1ae5` |
| `Alignment` | alignment flags (child names) (`0x4ecbf6`) | +0x14 | 0 (base) | confirmed (78) | `0x4f1b0f` |
| `Labelalignment` | alignment flags (child names) (`0x4ecbf6`) | +0x70 | -1 | confirmed (21) | `0x4f1b32` |
| `LabelIndent` | int (atol) (`0x7720c0`) | +0x60 | 0 | confirmed (21) | `0x4f1b55` |
| `Templatename` | string, max 31 chars (`0x4ec159`) | +0x20 | empty | confirmed (148) | `0x4f1b7f` |
| `Label` | text, max 31 chars, UTF-8 to ANSI (0x4eca97) (`0x4eca97 + 0x4ec159`) | +0x40 | empty | confirmed (117) | `0x4f1ba1` |
| `Onclickevent` | **event**: stock name or leading-digit number (`0x4ea06f`) | +0x68 | -1 | confirmed (100) | `0x4f1bca` |
| `Onshiftclickevent` | **event**: stock name or leading-digit number (`0x4ea06f`) | +0x6c | not set | decompiler only | `0x4f1bf3` |
| `Adapter` | string, max 31 chars (`0x4ec159`) | +0x74 | empty | decompiler only | `0x4f1c1c` |
| `Coloradapter` | string, max 31 chars (`0x4ec159`) | +0x94 | empty | decompiler only | `0x4f1c3b` |
| `ToolTipID` | int (atol) (`0x7720c0`) | +0x18 | 0 (base) | confirmed (12) | `0x4f1c74` |
| `Priority` | int (atol) (`0x7720c0`) | +0x1c | 0 (base) | decompiler only | `0x4f1c9b` |

### HorizontalResizeImageButtonDef

Parser `0x4f1cf7`. Object `new(0xdc)`, constructor `0x4eca2e` (base `0x4ec177`). 12 of 16 child elements appear in stock XML.

| Child element | Value type (mapper) | Stored at | Default from constructor | Stock XML | Check |
|---|---|---|---|---|---|
| `Controlid` | ControlId: name or leading-digit number (`0x4e99e6`) | +0x8 | -1 (base) | confirmed (103) | `0x4f1d3c` |
| `Position` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0xc | 0 (base) | confirmed (103) | `0x4f1d6f` |
| `ImageOffset` | point: `<X>` `<Y>` children, atol (`0x4ecb86`) | +0xd4 | not set | confirmed (101) | `0x4f1d95` |
| `Width` | int (atol) (`0x7720c0`) | +0x64 | 64 | confirmed (103) | `0x4f1db1` |
| `Alignment` | alignment flags (child names) (`0x4ecbf6`) | +0x14 | 0 (base) | confirmed (7) | `0x4f1ddb` |
| `Labelalignment` | alignment flags (child names) (`0x4ecbf6`) | +0x70 | -1 | confirmed (37) | `0x4f1dfe` |
| `LabelIndent` | int (atol) (`0x7720c0`) | +0x60 | 0 | confirmed (37) | `0x4f1e21` |
| `HRButtonTemplateName` | string, max 31 chars (`0x4ec159`) | +0x20 | empty | confirmed (103) | `0x4f1e4b` |
| `ImageAreaTemplateName` | string, max 31 chars (`0x4ec159`) | +0xb4 | empty | confirmed (103) | `0x4f1e6d` |
| `Label` | text, max 31 chars, UTF-8 to ANSI (0x4eca97) (`0x4eca97 + 0x4ec159`) | +0x40 | empty | confirmed (101) | `0x4f1e92` |
| `Onclickevent` | **event**: stock name or leading-digit number (`0x4ea06f`) | +0x68 | -1 | confirmed (101) | `0x4f1ebb` |
| `Onshiftclickevent` | **event**: stock name or leading-digit number (`0x4ea06f`) | +0x6c | not set | decompiler only | `0x4f1ee4` |
| `Adapter` | string, max 31 chars (`0x4ec159`) | +0x74 | empty | decompiler only | `0x4f1f0d` |
| `Coloradapter` | string, max 31 chars (`0x4ec159`) | +0x94 | empty | decompiler only | `0x4f1f2c` |
| `ToolTipID` | int (atol) (`0x7720c0`) | +0x18 | 0 (base) | confirmed (58) | `0x4f1f65` |
| `Priority` | int (atol) (`0x7720c0`) | +0x1c | 0 (base) | decompiler only | `0x4f1f8c` |

## 5. Name tables

### 5.1 ControlId (`0x4E99E6`)

On this build `0x4E99E6` is `jmp 0x2483000` into the `.raid` section. The stub
maps `RaidMember00`-`RaidMember79` to `0x600`-`0x64F`. It then replays the
overwritten `push "Unspecified"` and jumps back to `0x4E99EB` (`0x2483780`),
so the stock chain is intact:

| ID | Name |
|---|---|
| -1 | `Unspecified` |
| 100 | `ButtonMove` |
| 101 | `ButtonClose` |
| 102 | `ButtonMinimize` |
| 103 | `ButtonInfo` |
| 104 | `TitleText` |
| 105 | `BackgroundImage` |
| 106 | `VerticalResizeHandleTop` |
| 107 | `VerticalResizeHandleBottom` |
| 108 | `HorizontalResizeHandleLeft` |
| 109 | `HorizontalResizehandleRight` |
| 110 | `TwoWayResizeTopLeft` |
| 111 | `TwoWayResizeTopRight` |
| 112 | `TwoWayResizeBottomLeft` |
| 113 | `TwoWayResizeBottomRight` |

Anything else: a first-byte digit gives `atol` (`0x4E9B36`-`0x4E9B4B`),
otherwise -1. Stock XML mostly uses numbers (for example `1020`). Stock also
uses `Background` (64), `ImageBackground` (6) and `c_controlBackgroundImage`
(4), which are not in the table and become -1.

### 5.2 WindowId (`0x4E9B50`)

| ID | Name |
|---|---|
| -1 | `Unspecified` |
| 0 | `Command` |
| 1 | `Timer` |
| 2 | `Clock` |
| 3 | `Interact` |
| 4 | `Quest` |
| 5 | `Info` |
| 6 | `MiniFriends` |
| 7 | `MiniGroup` |
| 8 | `MiniCraft` |
| 9 | `MiniInfo` |
| 10 | `Concentration` |
| 11 | `Chat` |
| 12 | `Vault` |
| 13 | `ChatConfig` |
| 14 | `Train` |
| 15 | `Compass` |
| 16 | `Merchant` |
| 17 | `ChannelPicker` |
| 18 | `EmblemPicker` |
| 19 | `GroupFinder` |
| 20 | `Help` |
| 22 | `AlphaPicker` |
| 23 | `Performance` |
| 24 | `OptionsDialog` |
| 25 | `Stats` |
| 26 | `Summary` |
| 28 | `MasterLevel` |
| 29 | `Quiver` |
| 30 | `KeyConfig` |
| 31 | `MinoRelic` |
| 33 | `NewQuest` |
| 34 | `NewQuestFinish` |
| 35 | `Emoticon` |
| 36 | `Channel` |
| 37 | `Context` |
| 38 | `ContextSub` |
| 39 | `ContextChat` |
| 40 | `ContextEmoticon` |
| 41 | `ContextWindowAlpha` |
| 42 | `ContextFontAlpha` |
| 43 | `ContextFontSize` |
| 44 | `ContextDefaultChannel` |
| 48 | `chatRenameWindow` |
| 49 | `BazaarQuery` |
| 50 | `MainMenu` |
| 51 | `InterfaceElements` |
| 52 | `MenuBar` |
| 53 | `NewGroup` |
| 54 | `NewSummary` |
| 55 | `FloatTarget` |
| 56 | `FloatLevelXP` |
| 57 | `FloatChampXP` |
| 58 | `FloatRealmXP` |
| 59 | `Release` |

Anything else: a first-byte digit gives `atol` (`0x4EA055`-`0x4EA06A`),
otherwise -1. 28 stock spellings are not in this table and become -1, among
them `main_menu`, `menu_bar`, `float_target`, `Options`, `MiniPet`, `Mount`,
`Title`, `Training` and `c_windowInvalid`. Several differ from a mapped name
only by underscores (`main_menu` vs `MainMenu`). Whether those windows rely
on -1 is not known.

## 6. Stock OnClickEvent names (`0x4EA06F`)

The mapper is a chain of 365 `_stricmp` compares (`0x77156E`, ASCII
case-insensitive) from `0x4EA06F` to `0x4EC139`. The first compare,
`Unspecified`, returns -1. The other **364 names** return the IDs below: 1 to
582 (`0x246`), all distinct and none at or above `0x600`. 218 values in that
range are unused. (Earlier notes said "218 names"; the code has 364, plus
`Unspecified`.) After the chain, `0x4EC13F` returns `atol` when the first byte
is a digit, otherwise -1.

The 21 call sites (all `call 0x4EA06F`, one per event child element):
ButtonDef `0x4ED8A3`/`0x4ED8CC`, InvisibleButtonDef `0x4EDA7F`/`0x4EDAA5`,
ExpandableMenuDef and ComboBoxDef `Onchangeevent`, ListBoxDef
`OnHeaderClickEvent`/`OnClickEvent`/`OnShiftClickEvent`, IconDef, CheckBoxDef
(2), DockableIconDef, HorizontalSliderDef, VerticalSliderDef, TabsDef (2),
HorizontalResizeButtonDef (2) and HorizontalResizeImageButtonDef (2). Their
check addresses are in §4.

Stock XML uses 209 distinct event values, all names. **No stock file uses a
numeric event.** 201 values are in the table. These 8 are not, and resolve to
-1: `GroupFind`, `Mount` (stats windows), and `AlphaSettingsAccept`,
`AlphaSettingsCancel`, `FontAlphaIncrease`, `FontAlphaDecrease`,
`WindowAlphaIncrease`, `WindowAlphaDecrease` (`isles/alpha_chooser.xml`).
"Stock" below marks the names stock XML uses.

An event of -1 does not mean a dead button. 229 stock buttons have no event
or an empty one, including every ACCEPT/CANCEL/SET button. Their windows
handle the click by ControlId instead. The `GroupFind` buttons (FIND 1003,
SET LFG 1004, `stats_group_window.xml`) and `Mount` (MOUNT 1030,
`stats_attributes_window.xml`) carry ControlIds the same way, and FIND and
SET LFG share one event name, so the name is evidently decorative.
`isles/alpha_chooser.xml` is not included by any `uimain.xml` or other file,
so the client never loads it. None of these is a known defect; the
ControlId dispatch itself has not been traced.

| ID | Hex | Name | Stock | Compare |
|---|---|---|---|---|
| -1 | `0xffffffff` | `Unspecified` |  | `0x4ea06f` |
| 100 | `0x64` | `ToggleAttackMode` | yes | `0x4ea084` |
| 101 | `0x65` | `Realm` | yes | `0x4ea0b3` |
| 102 | `0x66` | `Sprint` | yes | `0x4ea0cb` |
| 103 | `0x67` | `ToggleSitting` | yes | `0x4ea0e3` |
| 104 | `0x68` | `Task` | yes | `0x4ea0fb` |
| 105 | `0x69` | `Follow` | yes | `0x4ea113` |
| 106 | `0x6a` | `Stick` | yes | `0x4ea12b` |
| 107 | `0x6b` | `Afk` | yes | `0x4ea143` |
| 108 | `0x6c` | `Face` | yes | `0x4ea15b` |
| 109 | `0x6d` | `Anon` |  | `0x4ea173` |
| 110 | `0x6e` | `Pray` | yes | `0x4ea18b` |
| 111 | `0x6f` | `Help` | yes | `0x4ea1a3` |
| 112 | `0x70` | `Release` | yes | `0x4ea1bb` |
| 113 | `0x71` | `Relic` | yes | `0x4ea1d3` |
| 114 | `0x72` | `Bonuses` | yes | `0x4ea1eb` |
| 115 | `0x73` | `ShowKeyboardConfig` | yes | `0x4ea203` |
| 116 | `0x74` | `Journal` | yes | `0x4ea21b` |
| 117 | `0x75` | `Friends` | yes | `0x4ea233` |
| 118 | `0x76` | `Bind` | yes | `0x4ea24b` |
| 119 | `0x77` | `ToggleCompass` | yes | `0x4ea263` |
| 120 | `0x78` | `Quit` | yes | `0x4ea27b` |
| 121 | `0x79` | `Interact` |  | `0x4ea293` |
| 122 | `0x7a` | `SelectGroupMember0` | yes | `0x4ea2a8` |
| 123 | `0x7b` | `SelectGroupMember1` | yes | `0x4ea2bd` |
| 124 | `0x7c` | `SelectGroupMember2` | yes | `0x4ea2d2` |
| 125 | `0x7d` | `SelectGroupMember3` | yes | `0x4ea2e7` |
| 126 | `0x7e` | `SelectGroupMember4` | yes | `0x4ea2fc` |
| 127 | `0x7f` | `SelectGroupMember5` | yes | `0x4ea311` |
| 128 | `0x80` | `SelectGroupMember6` | yes | `0x4ea326` |
| 129 | `0x81` | `SelectGroupMember7` | yes | `0x4ea33d` |
| 130 | `0x82` | `Repair` | yes | `0x4ea354` |
| 131 | `0x83` | `Craft` | yes | `0x4ea36b` |
| 132 | `0x84` | `Salvage` | yes | `0x4ea382` |
| 133 | `0x85` | `RemoveConc` |  | `0x4ea399` |
| 134 | `0x86` | `ConcQuickDesc` |  | `0x4ea3b0` |
| 135 | `0x87` | `Train` | yes | `0x4ea3c7` |
| 136 | `0x88` | `TrainInfo` | yes | `0x4ea3de` |
| 137 | `0x89` | `NewTrain` | yes | `0x4ea3f5` |
| 138 | `0x8a` | `NewTrainInfo` | yes | `0x4ea40c` |
| 141 | `0x8d` | `NewTrainRealmInfo` | yes | `0x4ea423` |
| 148 | `0x94` | `GroupQuerySolo` |  | `0x4ea43a` |
| 149 | `0x95` | `GroupQueryGroup` |  | `0x4ea451` |
| 150 | `0x96` | `GroupSoloSeeking` |  | `0x4ea468` |
| 151 | `0x97` | `GroupSoloNotSeeking` |  | `0x4ea47f` |
| 152 | `0x98` | `GroupSeekingNone` |  | `0x4ea496` |
| 153 | `0x99` | `GroupSeekingAll` |  | `0x4ea4ad` |
| 154 | `0x9a` | `GroupSeekingFighter` |  | `0x4ea4c4` |
| 155 | `0x9b` | `GroupSeekingMage` |  | `0x4ea4db` |
| 156 | `0x9c` | `GroupSeekingHealer` |  | `0x4ea4f2` |
| 157 | `0x9d` | `GroupSeekingRogue` |  | `0x4ea509` |
| 158 | `0x9e` | `GroupInvite` | yes | `0x4ea520` |
| 159 | `0x9f` | `GroupDisband` | yes | `0x4ea537` |
| 160 | `0xa0` | `SiegeArm` |  | `0x4ea54e` |
| 161 | `0xa1` | `SiegeLoad` |  | `0x4ea565` |
| 162 | `0xa2` | `SiegeAim` | yes | `0x4ea57c` |
| 163 | `0xa3` | `SiegeFire` | yes | `0x4ea593` |
| 165 | `0xa5` | `SiegeRelease` | yes | `0x4ea5aa` |
| 166 | `0xa6` | `SiegeRepair` | yes | `0x4ea5c1` |
| 167 | `0xa7` | `SiegeSalvage` | yes | `0x4ea5d8` |
| 168 | `0xa8` | `SiegeMove` | yes | `0x4ea5ef` |
| 169 | `0xa9` | `SiegeAddAmmo` |  | `0x4ea606` |
| 170 | `0xaa` | `PetSetMoveState` |  | `0x4ea61d` |
| 171 | `0xab` | `PetSetCombatState` |  | `0x4ea634` |
| 172 | `0xac` | `PetAttack` | yes | `0x4ea64b` |
| 173 | `0xad` | `PetRelease` | yes | `0x4ea662` |
| 174 | `0xae` | `PetTarget` | yes | `0x4ea679` |
| 175 | `0xaf` | `QuiverSelect0` | yes | `0x4ea690` |
| 176 | `0xb0` | `QuiverSelect1` | yes | `0x4ea6a7` |
| 177 | `0xb1` | `QuiverSelect2` | yes | `0x4ea6be` |
| 178 | `0xb2` | `QuiverSelect3` | yes | `0x4ea6d5` |
| 179 | `0xb3` | `QuiverSelectInventory` |  | `0x4ea6ec` |
| 180 | `0xb4` | `QuiverAdd0` | yes | `0x4ea703` |
| 181 | `0xb5` | `QuiverAdd1` | yes | `0x4ea71a` |
| 182 | `0xb6` | `QuiverAdd2` | yes | `0x4ea731` |
| 183 | `0xb7` | `QuiverAdd3` | yes | `0x4ea748` |
| 184 | `0xb8` | `QuiverDump` |  | `0x4ea75f` |
| 188 | `0xbc` | `ValueChooserDecrease` | yes | `0x4eb0ce` |
| 189 | `0xbd` | `ValueChooserIncrease` | yes | `0x4eb0e5` |
| 190 | `0xbe` | `ValueChooserShiftDecrease` | yes | `0x4eb12a` |
| 191 | `0xbf` | `ValueChooserShiftIncrease` | yes | `0x4eb141` |
| 192 | `0xc0` | `ValueChooserAccept` | yes | `0x4eb0fc` |
| 193 | `0xc1` | `ValueChooserCancel` | yes | `0x4eb113` |
| 200 | `0xc8` | `ToggleStatsSkills` | yes | `0x4ea776` |
| 201 | `0xc9` | `ToggleStatsInventory` | yes | `0x4ea78d` |
| 202 | `0xca` | `ToggleStatsAttributes` | yes | `0x4ea7a4` |
| 203 | `0xcb` | `ToggleStatsGroup` | yes | `0x4ea7bb` |
| 204 | `0xcc` | `ToggleStatsSpells` | yes | `0x4ea7d2` |
| 205 | `0xcd` | `ToggleStatsStyles` | yes | `0x4ea7e9` |
| 206 | `0xce` | `ToggleStatsIndex` |  | `0x4ea800` |
| 207 | `0xcf` | `ToggleMiniGroup` | yes | `0x4ea817` |
| 208 | `0xd0` | `ToggleConcentration` | yes | `0x4ea82e` |
| 209 | `0xd1` | `ToggleJournal` | yes | `0x4ea845` |
| 210 | `0xd2` | `ToggleOptions` | yes | `0x4ea85c` |
| 211 | `0xd3` | `ToggleMiniPet` | yes | `0x4ea873` |
| 212 | `0xd4` | `ToggleGroupPicker` |  | `0x4ea88a` |
| 213 | `0xd5` | `ToggleHelp` | yes | `0x4ea8a1` |
| 214 | `0xd6` | `ToggleMiniFriends` | yes | `0x4ea8b8` |
| 215 | `0xd7` | `TogglePerformanceMeter` | yes | `0x4ea8cf` |
| 216 | `0xd8` | `ToggleCompassWindow` | yes | `0x4ea8e6` |
| 217 | `0xd9` | `ToggleCommandWindow` | yes | `0x4ea8fd` |
| 218 | `0xda` | `ToggleClock` | yes | `0x4ea914` |
| 219 | `0xdb` | `ToggleMasterLevel` | yes | `0x4ea92b` |
| 220 | `0xdc` | `ToggleQuiver` | yes | `0x4ea942` |
| 221 | `0xdd` | `ToggleGroupBuffs` | yes | `0x4ea959` |
| 222 | `0xde` | `ToggleHibWarmap` |  | `0x4ea970` |
| 223 | `0xdf` | `ToggleMidWarmap` |  | `0x4ea987` |
| 224 | `0xe0` | `ToggleAlbWarmap` |  | `0x4ea99e` |
| 225 | `0xe1` | `ToggleAgramonWarmap` |  | `0x4ea9b5` |
| 226 | `0xe2` | `ToggleWarmapKey` | yes | `0x4ea9cc` |
| 227 | `0xe3` | `ToggleBonuses` |  | `0x4ea9e3` |
| 228 | `0xe4` | `ToggleMap` | yes | `0x4ea9fa` |
| 229 | `0xe5` | `ToggleMainMenu` | yes | `0x4eaa11` |
| 230 | `0xe6` | `ToggleInterfaceElements` | yes | `0x4eaa28` |
| 231 | `0xe7` | `ToggleMenuBar` | yes | `0x4eaa3f` |
| 232 | `0xe8` | `ToggleNewGroup` | yes | `0x4eaa56` |
| 233 | `0xe9` | `ToggleSummary` | yes | `0x4eaa6d` |
| 234 | `0xea` | `ToggleNewSummary` | yes | `0x4eaa84` |
| 235 | `0xeb` | `ToggleFloatTarget` | yes | `0x4eaa9b` |
| 236 | `0xec` | `ToggleFloatLevelXP` | yes | `0x4eaab2` |
| 237 | `0xed` | `ToggleFloatChampXP` | yes | `0x4eaac9` |
| 238 | `0xee` | `ToggleFloatRealmXP` | yes | `0x4eaae0` |
| 239 | `0xef` | `ToggleMount` | yes | `0x4eaaf7` |
| 240 | `0xf0` | `ToggleTitle` | yes | `0x4eab0e` |
| 241 | `0xf1` | `ToggleCasualGroupFinder` | yes | `0x4eab25` |
| 242 | `0xf2` | `ToggleGroupOptions` | yes | `0x4eab3c` |
| 243 | `0xf3` | `ToggleCustom0` |  | `0x4eab53` |
| 244 | `0xf4` | `ToggleCustom1` |  | `0x4eab6a` |
| 245 | `0xf5` | `ToggleCustom2` |  | `0x4eab81` |
| 246 | `0xf6` | `ToggleCustom3` |  | `0x4eab98` |
| 247 | `0xf7` | `ToggleCustom4` |  | `0x4eabaf` |
| 248 | `0xf8` | `ToggleCustom5` |  | `0x4eabc6` |
| 249 | `0xf9` | `ToggleCustom6` |  | `0x4eabdd` |
| 250 | `0xfa` | `ToggleCustom7` |  | `0x4eabf4` |
| 251 | `0xfb` | `ToggleCustom8` |  | `0x4eac0b` |
| 252 | `0xfc` | `ToggleCustom9` |  | `0x4eac22` |
| 253 | `0xfd` | `ToggleCustom10` |  | `0x4eac39` |
| 254 | `0xfe` | `ToggleCustom11` |  | `0x4eac50` |
| 255 | `0xff` | `ToggleCustom12` |  | `0x4eac67` |
| 256 | `0x100` | `ToggleCustom13` |  | `0x4eac7e` |
| 257 | `0x101` | `ToggleCustom14` |  | `0x4eac95` |
| 258 | `0x102` | `ToggleCustom15` |  | `0x4eacac` |
| 259 | `0x103` | `ToggleCustom16` |  | `0x4eacc3` |
| 260 | `0x104` | `ToggleCustom17` |  | `0x4eacda` |
| 261 | `0x105` | `ToggleCustom18` |  | `0x4eacf1` |
| 262 | `0x106` | `ToggleCustom19` |  | `0x4ead08` |
| 263 | `0x107` | `ShowCustom0` |  | `0x4ead1f` |
| 264 | `0x108` | `ShowCustom1` |  | `0x4ead36` |
| 265 | `0x109` | `ShowCustom2` |  | `0x4ead4d` |
| 266 | `0x10a` | `ShowCustom3` |  | `0x4ead64` |
| 267 | `0x10b` | `ShowCustom4` |  | `0x4ead7b` |
| 268 | `0x10c` | `ShowCustom5` |  | `0x4ead92` |
| 269 | `0x10d` | `ShowCustom6` |  | `0x4eada9` |
| 270 | `0x10e` | `ShowCustom7` |  | `0x4eadc0` |
| 271 | `0x10f` | `ShowCustom8` |  | `0x4eadd7` |
| 272 | `0x110` | `ShowCustom9` |  | `0x4eadee` |
| 273 | `0x111` | `ShowCustom10` |  | `0x4eae05` |
| 274 | `0x112` | `ShowCustom11` |  | `0x4eae1c` |
| 275 | `0x113` | `ShowCustom12` |  | `0x4eae33` |
| 276 | `0x114` | `ShowCustom13` |  | `0x4eae4a` |
| 277 | `0x115` | `ShowCustom14` |  | `0x4eae61` |
| 278 | `0x116` | `ShowCustom15` |  | `0x4eae78` |
| 279 | `0x117` | `ShowCustom16` |  | `0x4eae8f` |
| 280 | `0x118` | `ShowCustom17` |  | `0x4eaea6` |
| 281 | `0x119` | `ShowCustom18` |  | `0x4eaebd` |
| 282 | `0x11a` | `ShowCustom19` |  | `0x4eaed4` |
| 283 | `0x11b` | `HideCustom0` |  | `0x4eaf02` |
| 284 | `0x11c` | `HideCustom1` |  | `0x4eaf19` |
| 285 | `0x11d` | `HideCustom2` |  | `0x4eaf30` |
| 286 | `0x11e` | `HideCustom3` |  | `0x4eaf47` |
| 287 | `0x11f` | `HideCustom4` |  | `0x4eaf5e` |
| 288 | `0x120` | `HideCustom5` |  | `0x4eaf75` |
| 289 | `0x121` | `HideCustom6` |  | `0x4eaf8c` |
| 290 | `0x122` | `HideCustom7` |  | `0x4eafa3` |
| 291 | `0x123` | `HideCustom8` |  | `0x4eafba` |
| 292 | `0x124` | `HideCustom9` |  | `0x4eafd1` |
| 293 | `0x125` | `HideCustom10` |  | `0x4eafe8` |
| 294 | `0x126` | `HideCustom11` |  | `0x4eafff` |
| 295 | `0x127` | `HideCustom12` |  | `0x4eb016` |
| 296 | `0x128` | `HideCustom13` |  | `0x4eb02d` |
| 297 | `0x129` | `HideCustom14` |  | `0x4eb044` |
| 298 | `0x12a` | `HideCustom15` |  | `0x4eb05b` |
| 299 | `0x12b` | `HideCustom16` |  | `0x4eb072` |
| 300 | `0x12c` | `HideCustom17` |  | `0x4eb089` |
| 301 | `0x12d` | `HideCustom18` |  | `0x4eb0a0` |
| 302 | `0x12e` | `HideCustom19` |  | `0x4eb0b7` |
| 303 | `0x12f` | `ShowWarmap` | yes | `0x4eaeeb` |
| 308 | `0x134` | `QuickBarEvent` |  | `0x4eb158` |
| 309 | `0x135` | `QuickBarQuickDesc` |  | `0x4eb16f` |
| 310 | `0x136` | `MasterLevelPageNext` | yes | `0x4eb186` |
| 311 | `0x137` | `MasterLevelPagePrevious` | yes | `0x4eb19d` |
| 312 | `0x138` | `TradeAccept` |  | `0x4eb1b4` |
| 313 | `0x139` | `TradeCancel` |  | `0x4eb1cb` |
| 315 | `0x13b` | `MoneySelectorPIncrease` |  | `0x4eb1e2` |
| 316 | `0x13c` | `MoneySelectorPDecrease` |  | `0x4eb1f9` |
| 317 | `0x13d` | `MoneySelectorPLargeIncrease` |  | `0x4eb210` |
| 318 | `0x13e` | `MoneySelectorPLargeDecrease` |  | `0x4eb227` |
| 319 | `0x13f` | `MoneySelectorGIncrease` |  | `0x4eb23e` |
| 320 | `0x140` | `MoneySelectorGDecrease` |  | `0x4eb255` |
| 321 | `0x141` | `MoneySelectorGLargeIncrease` |  | `0x4eb26c` |
| 322 | `0x142` | `MoneySelectorGLargeDecrease` |  | `0x4eb283` |
| 323 | `0x143` | `MoneySelectorSIncrease` |  | `0x4eb29a` |
| 324 | `0x144` | `MoneySelectorSDecrease` |  | `0x4eb2b1` |
| 325 | `0x145` | `MoneySelectorSLargeIncrease` |  | `0x4eb2c8` |
| 326 | `0x146` | `MoneySelectorSLargeDecrease` |  | `0x4eb2df` |
| 327 | `0x147` | `MoneySelectorCIncrease` |  | `0x4eb2f6` |
| 328 | `0x148` | `MoneySelectorCDecrease` |  | `0x4eb30d` |
| 329 | `0x149` | `MoneySelectorCLargeIncrease` |  | `0x4eb324` |
| 330 | `0x14a` | `MoneySelectorCLargeDecrease` |  | `0x4eb33b` |
| 331 | `0x14b` | `MoneySelectorPBatchIncrease` |  | `0x4eb352` |
| 332 | `0x14c` | `MoneySelectorPBatchDecrease` |  | `0x4eb369` |
| 333 | `0x14d` | `MoneySelectorPBatchLargeIncrease` |  | `0x4eb380` |
| 334 | `0x14e` | `MoneySelectorPBatchLargeDecrease` |  | `0x4eb397` |
| 335 | `0x14f` | `MoneySelectorGBatchIncrease` |  | `0x4eb3ae` |
| 336 | `0x150` | `MoneySelectorGBatchDecrease` |  | `0x4eb3c5` |
| 337 | `0x151` | `MoneySelectorGBatchLargeIncrease` |  | `0x4eb3dc` |
| 338 | `0x152` | `MoneySelectorGBatchLargeDecrease` |  | `0x4eb3f3` |
| 339 | `0x153` | `MoneySelectorSBatchIncrease` |  | `0x4eb40a` |
| 340 | `0x154` | `MoneySelectorSBatchDecrease` |  | `0x4eb421` |
| 341 | `0x155` | `MoneySelectorSBatchLargeIncrease` |  | `0x4eb438` |
| 342 | `0x156` | `MoneySelectorSBatchLargeDecrease` |  | `0x4eb44f` |
| 343 | `0x157` | `MoneySelectorCBatchIncrease` |  | `0x4eb466` |
| 344 | `0x158` | `MoneySelectorCBatchDecrease` |  | `0x4eb47d` |
| 345 | `0x159` | `MoneySelectorCBatchLargeIncrease` |  | `0x4eb494` |
| 346 | `0x15a` | `MoneySelectorCBatchLargeDecrease` |  | `0x4eb4ab` |
| 347 | `0x15b` | `KeepUpgradeLevelInc` | yes | `0x4eb4c2` |
| 348 | `0x15c` | `KeepUpgradeLevelDec` | yes | `0x4eb4d9` |
| 349 | `0x15d` | `KeepUpgradeRelease` | yes | `0x4eb4f0` |
| 350 | `0x15e` | `KeepUpgradeCancel` | yes | `0x4eb51e` |
| 351 | `0x15f` | `KeepUpgradeAccept` | yes | `0x4eb507` |
| 352 | `0x160` | `KeepStatusHookpoints` | yes | `0x4eb535` |
| 353 | `0x161` | `KeepStatusRepair` | yes | `0x4eb54c` |
| 354 | `0x162` | `HookpointStoreCoin` | yes | `0x4eb563` |
| 355 | `0x163` | `HookpointStoreBP` | yes | `0x4eb57a` |
| 356 | `0x164` | `HookpointStoreGBP` | yes | `0x4eb591` |
| 357 | `0x165` | `HookpointStoreContract` | yes | `0x4eb5a8` |
| 358 | `0x166` | `HookpointStoreBuy` | yes | `0x4eb5bf` |
| 359 | `0x167` | `WarmapTowerSelect0` | yes | `0x4eb5d6` |
| 360 | `0x168` | `WarmapTowerSelect1` |  | `0x4eb5ed` |
| 361 | `0x169` | `WarmapTowerSelect2` |  | `0x4eb604` |
| 362 | `0x16a` | `WarmapTowerSelect3` |  | `0x4eb61b` |
| 363 | `0x16b` | `WarmapTowerSelect4` |  | `0x4eb632` |
| 364 | `0x16c` | `WarmapTowerSelect5` |  | `0x4eb649` |
| 365 | `0x16d` | `WarmapTowerSelect6` |  | `0x4eb660` |
| 366 | `0x16e` | `WarmapTowerSelect7` |  | `0x4eb677` |
| 367 | `0x16f` | `WarmapTowerSelect8` |  | `0x4eb68e` |
| 368 | `0x170` | `WarmapTowerSelect9` |  | `0x4eb6a5` |
| 369 | `0x171` | `WarmapTowerSelect10` |  | `0x4eb6bc` |
| 370 | `0x172` | `WarmapTowerSelect11` |  | `0x4eb6d3` |
| 371 | `0x173` | `WarmapTowerSelect12` |  | `0x4eb6ea` |
| 372 | `0x174` | `WarmapTowerSelect13` |  | `0x4eb701` |
| 373 | `0x175` | `WarmapTowerSelect14` |  | `0x4eb718` |
| 374 | `0x176` | `WarmapTowerSelect15` |  | `0x4eb72f` |
| 375 | `0x177` | `WarmapTowerSelect16` |  | `0x4eb746` |
| 376 | `0x178` | `WarmapTowerSelect17` |  | `0x4eb75d` |
| 377 | `0x179` | `WarmapTowerSelect18` |  | `0x4eb774` |
| 378 | `0x17a` | `WarmapTowerSelect19` |  | `0x4eb78b` |
| 379 | `0x17b` | `WarmapTowerSelect20` |  | `0x4eb7a2` |
| 380 | `0x17c` | `WarmapTowerSelect21` |  | `0x4eb7b9` |
| 381 | `0x17d` | `WarmapTowerSelect22` |  | `0x4eb7d0` |
| 382 | `0x17e` | `WarmapTowerSelect23` |  | `0x4eb7e7` |
| 383 | `0x17f` | `WarmapTowerSelect24` |  | `0x4eb7fe` |
| 384 | `0x180` | `WarmapTowerSelect25` |  | `0x4eb815` |
| 385 | `0x181` | `WarmapTowerSelect26` |  | `0x4eb82c` |
| 386 | `0x182` | `WarmapTowerSelect27` |  | `0x4eb843` |
| 387 | `0x183` | `WarmapKeepSelect0` | yes | `0x4eb85a` |
| 388 | `0x184` | `WarmapKeepSelect1` | yes | `0x4eb871` |
| 389 | `0x185` | `WarmapKeepSelect2` | yes | `0x4eb888` |
| 390 | `0x186` | `WarmapKeepSelect3` | yes | `0x4eb89f` |
| 391 | `0x187` | `WarmapKeepSelect4` | yes | `0x4eb8b6` |
| 392 | `0x188` | `WarmapKeepSelect5` | yes | `0x4eb8cd` |
| 393 | `0x189` | `WarmapKeepSelect6` | yes | `0x4eb8e4` |
| 394 | `0x18a` | `WarmapPowerRelicSelect` | yes | `0x4eb8fb` |
| 395 | `0x18b` | `WarmapStrengthRelicSelect` | yes | `0x4eb912` |
| 396 | `0x18c` | `WarmapPowerBorderSelect` | yes | `0x4eb929` |
| 397 | `0x18d` | `WarmapStrengthBorderSelect` | yes | `0x4eb940` |
| 399 | `0x18f` | `WarmapUpdateBattles` | yes | `0x4eb96e` |
| 400 | `0x190` | `WarmapTeleportRequest` | yes | `0x4eb957` |
| 401 | `0x191` | `WarmapUpdateBonuses` | yes | `0x4eb985` |
| 402 | `0x192` | `InGameKeyConfigAccept` | yes | `0x4eb99c` |
| 403 | `0x193` | `InGameKeyConfigCancel` | yes | `0x4eb9b3` |
| 404 | `0x194` | `InGameKeyConfigClearKey` | yes | `0x4eb9ca` |
| 405 | `0x195` | `InGameKeyConfigResetFPS` | yes | `0x4eb9e1` |
| 406 | `0x196` | `InGameKeyConfigResetRPG` | yes | `0x4eb9f8` |
| 407 | `0x197` | `InGameKeyConfigResetMMO` | yes | `0x4eba0f` |
| 408 | `0x198` | `SetTitle` |  | `0x4eba26` |
| 409 | `0x199` | `Title` |  | `0x4eba3d` |
| 410 | `0x19a` | `MiniInfoUse` |  | `0x4eba54` |
| 411 | `0x19b` | `MiniInfoMakeUseIcon` |  | `0x4eba82` |
| 412 | `0x19c` | `MiniInfoUse2` |  | `0x4eba6b` |
| 413 | `0x19d` | `MiniInfoMakeUse2Icon` |  | `0x4eba99` |
| 414 | `0x19e` | `MiniInfoDelve` |  | `0x4ebab0` |
| 415 | `0x19f` | `Community` | yes | `0x4ebac7` |
| 417 | `0x1a1` | `ComTabChanged` | yes | `0x4ebade` |
| 418 | `0x1a2` | `ComTabRefresh` | yes | `0x4ebaf5` |
| 419 | `0x1a3` | `ComGuildMembersNext` | yes | `0x4ebb0c` |
| 420 | `0x1a4` | `ComGuildMembersPrev` | yes | `0x4ebb23` |
| 421 | `0x1a5` | `ComGuildMembersSort` | yes | `0x4ebb3a` |
| 422 | `0x1a6` | `ComGuildMemberSelected` | yes | `0x4ebb51` |
| 423 | `0x1a7` | `ComGuildMembersShowOffline` | yes | `0x4ebb68` |
| 424 | `0x1a8` | `ComGuildPromote` | yes | `0x4ebb7f` |
| 425 | `0x1a9` | `ComGuildDemote` | yes | `0x4ebb96` |
| 426 | `0x1aa` | `ComGuildNote` | yes | `0x4ebbad` |
| 427 | `0x1ab` | `ComGuildSend` | yes | `0x4ebbc4` |
| 428 | `0x1ac` | `ComGuildCommands` | yes | `0x4ebbdb` |
| 429 | `0x1ad` | `ComGuildBannerDeploy` | yes | `0x4ebbf2` |
| 430 | `0x1ae` | `ComGuildWithdraw` | yes | `0x4ebc09` |
| 433 | `0x1b1` | `ComAllianceMembersNext` | yes | `0x4ebc20` |
| 434 | `0x1b2` | `ComAllianceMembersPrev` | yes | `0x4ebc37` |
| 435 | `0x1b3` | `ComAllianceMembersSort` | yes | `0x4ebc4e` |
| 436 | `0x1b4` | `ComAllianceMemberSelected` | yes | `0x4ebc65` |
| 437 | `0x1b5` | `ComAllianceMembersShowOffline` | yes | `0x4ebc7c` |
| 438 | `0x1b6` | `ComAllianceSend` | yes | `0x4ebc93` |
| 439 | `0x1b7` | `ComFriendsMembersNext` |  | `0x4ebcaa` |
| 440 | `0x1b8` | `ComFriendsMembersPrev` |  | `0x4ebcc1` |
| 441 | `0x1b9` | `ComFriendsMembersSort` | yes | `0x4ebcd8` |
| 442 | `0x1ba` | `ComFriendsMemberSelected` | yes | `0x4ebcef` |
| 443 | `0x1bb` | `ComFriendsSend` | yes | `0x4ebd06` |
| 444 | `0x1bc` | `ComLFGuildMembersSort` | yes | `0x4ebd1d` |
| 445 | `0x1bd` | `ComLFGuildMemberSelected` | yes | `0x4ebd34` |
| 446 | `0x1be` | `ComLFGuildSend` | yes | `0x4ebd62` |
| 447 | `0x1bf` | `ComLFGuildInvite` | yes | `0x4ebd4b` |
| 449 | `0x1c1` | `SummaryToggleExp` | yes | `0x4ebd90` |
| 450 | `0x1c2` | `MenuBarToggleExp` | yes | `0x4ebda7` |
| 451 | `0x1c3` | `MenuBarWithDraw` |  | `0x4ebdbe` |
| 455 | `0x1c7` | `SubclassDelve` | yes | `0x4ebd79` |
| 541 | `0x21d` | `RemoveQuest` | yes | `0x4ea09c` |
| 543 | `0x21f` | `Delve` | yes | `0x4ebdd5` |
| 544 | `0x220` | `NewQuestAccept` | yes | `0x4ebdec` |
| 545 | `0x221` | `NewQuestDecline` | yes | `0x4ebe03` |
| 546 | `0x222` | `NewQuestFinish` | yes | `0x4ebe1a` |
| 547 | `0x223` | `NewQuestContinue` | yes | `0x4ebe31` |
| 548 | `0x224` | `NewQuestSelectOptReward0` | yes | `0x4ebe48` |
| 549 | `0x225` | `NewQuestSelectOptReward1` | yes | `0x4ebe5f` |
| 550 | `0x226` | `NewQuestSelectOptReward2` | yes | `0x4ebe76` |
| 551 | `0x227` | `NewQuestSelectOptReward3` | yes | `0x4ebe8d` |
| 552 | `0x228` | `NewQuestSelectOptReward4` | yes | `0x4ebea4` |
| 553 | `0x229` | `NewQuestSelectOptReward5` | yes | `0x4ebebb` |
| 554 | `0x22a` | `NewQuestSelectOptReward6` | yes | `0x4ebed2` |
| 555 | `0x22b` | `NewQuestSelectOptReward7` | yes | `0x4ebee9` |
| 556 | `0x22c` | `MinoRelicDrop` | yes | `0x4ebf00` |
| 557 | `0x22d` | `ContextOnWindowAlpha` | yes | `0x4ebf17` |
| 558 | `0x22e` | `ContextOnFontAlpha` | yes | `0x4ebf2e` |
| 559 | `0x22f` | `ContextOnAddChatWindow` | yes | `0x4ebf45` |
| 560 | `0x230` | `ContextOnDeleteChatWindow` | yes | `0x4ebf5c` |
| 561 | `0x231` | `ContextOnRenameChatWindow` | yes | `0x4ebf73` |
| 562 | `0x232` | `ContextOnMessageAndChannel` | yes | `0x4ebf8a` |
| 563 | `0x233` | `ContextOnFontSize` | yes | `0x4ebfa1` |
| 564 | `0x234` | `ContextOnDefaultChannel` | yes | `0x4ebfb8` |
| 565 | `0x235` | `ContextSetWindowAlpha` | yes | `0x4ebfcf` |
| 566 | `0x236` | `ContextSetFontAlpha` | yes | `0x4ebfe6` |
| 567 | `0x237` | `ContextSetFontSize` | yes | `0x4ebffd` |
| 568 | `0x238` | `ContextSetDefaultChannel` | yes | `0x4ec014` |
| 569 | `0x239` | `ChatRenameOK` | yes | `0x4ec02b` |
| 570 | `0x23a` | `ChatRenameCancel` | yes | `0x4ec042` |
| 571 | `0x23b` | `AssistTarget` | yes | `0x4ec059` |
| 572 | `0x23c` | `ReleaseToBind` | yes | `0x4ec070` |
| 573 | `0x23d` | `ReleaseToCity` | yes | `0x4ec087` |
| 574 | `0x23e` | `ReleaseToHouse` | yes | `0x4ec09e` |
| 575 | `0x23f` | `RespecAll` | yes | `0x4ec0b5` |
| 576 | `0x240` | `RespecBuy` | yes | `0x4ec0cc` |
| 577 | `0x241` | `RespecRealm` | yes | `0x4ec0e3` |
| 578 | `0x242` | `QuickBind` | yes | `0x4ec0fa` |
| 581 | `0x245` | `WarmapPowerRelicTownSelect` |  | `0x4ec111` |
| 582 | `0x246` | `WarmapStrengthRelicTownSelect` |  | `0x4ec128` |

## 7. Numeric IDs versus stock names

| Value | Accepts a number? | Accepts a name? |
|---|---|---|
| All 21 event elements: `OnClickEvent` and `Onclickevent` (ButtonDef, InvisibleButtonDef, ListBoxDef, IconDef, CheckBoxDef, DockableIconDef, both sliders, TabsDef, both resize buttons), `OnShiftClickEvent` (ButtonDef, InvisibleButtonDef, ListBoxDef, CheckBoxDef, TabsDef, both resize buttons), `OnHeaderClickEvent` (ListBoxDef), `Onchangeevent` (ExpandableMenuDef, ComboBoxDef) | yes, if the text starts with a digit (`0x4EC13F`) | yes, the 364 names of §6 |
| `ControlId` / `ControlID` in all 36 controls | yes, if it starts with a digit | yes, the 14 names of §5.1, plus `RaidMember00`-`79` on this raid build |
| `WindowId` (window) | yes, if it starts with a digit | yes, the 55 names of §5.2 |
| TabsDef `<TabControl>` `<TabId>` / `<ControlId>`, `<Tab>` `<Id>` | yes (`atol` only) | **no** |
| Every int value of §2 (`Width`, `Iconid`, `ToolTipID`, `Priority`, ...) | yes (`atol`) | no |

No event element is parsed with plain `atol`, and none accepts only names.
Any event name the table does not know, such as `CompMgr30` or
`RaidMember00`, silently becomes -1 (no event). A decimal ID such as `1840`
passes through unchanged. Whether that ID *does* anything is up to the
client's event dispatch, not the parser.

## 8. XML alone versus a `game.dll` patch

**Possible with XML alone** (any client build):

- New windows: a `WindowTemplate` file added through `<Include>` in
  `uimain.xml`. Every control type, template, font, texture and canvas in this
  document is available.
- Layout and look: positions, sizes, alignment flags, colors (integers
  0-255), templates, fonts, label text up to 31 characters (use `LabelDef
  <Data>` for longer text), tooltips (`ToolTipID`), tabs.
- Clicks that trigger **stock** actions: any of the 364 names in §6, or the
  same action by decimal ID (for example `100` = `ToggleAttackMode`).
- Any integer as an event ID, ControlId or WindowId, by writing it in
  decimal. The parser accepts it. The raid click-to-target fix (0.33.0)
  works this way: decimal `1536`-`1615` reach the raid handler without
  parser changes.
- Built-in adapter names (`Adapter`, `Adaptername`, `Coloradapter`,
  `LabelAdapterName`, ...) for data the client already exposes.

**Needs a `game.dll` patch:**

- Making a **new** event ID do something. The parser passes it on, but only a
  handler decides what happens: the raid handler for `0x600`+n (`0x4E04DE`),
  the Companion Manager handler for `0x700`-`0x7DF`.
- New *names* for events, ControlIds or WindowIds (the raid hook at
  `0x4E99E6` is the example). Numbers avoid this need for the parser.
- New adapters, or server-driven text in a custom window (the manager's
  DebugMode label protocol).
- New element types or child elements, or different value syntax: a real
  boolean `1`, hex numbers, longer `Label` text in ButtonDef, right-click
  events on icons (`OnRightClickEvent` is never read), and IconDef
  `Width`/`Height`.
- New WindowId names. Stock spellings that map to -1 (`main_menu`,
  `menu_bar`, ...) belong to windows that evidently work, so -1 is a normal
  value there, not a defect to repair.

## 9. Template elements

Template parsers handle their nested containers in one function, so each
keyword list below is flattened in code order. Helpers seen in their
branches are `0x7720C0` (int), `0x4F259B` (bool), `0x4F239C` (color),
`0x4EC159` (string), `0x4F2498` and `0x4F2517` (`<X>`/`<Y>` pairs; the
second zeroes first), `0x4ECB86` (point), `0x4ECBF6` (alignment) and
`0x4F231D` (int). Container keywords such as `Font`, `Texture` and `Normal`
reach these helpers through shared tails. Their exact helper per keyword
is not tabulated. These lists come from a script and were spot-checked
(ButtonTemplate, ImageAreaTemplate, ListBoxTemplate), not verified row by
row.

| Element | Parser | Keyword compares (flattened, code order) |
|---|---|---|
| `TTFFont` | `0x4F25CD` | Name, File, Height, Antialiased, Outlined, Shadowed, Hint |
| `Font` | `0x4F2855` | Name, File |
| `GdiFont` | `0x4F2985` | Name, Face, TTF, Height, TextureSize, Bold, Antialiased, Outline, Shadow |
| `Texture` | `0x4F2B8F` | Name, File |
| `Canvas` | `0x4F2C88` | Name, File |
| `ButtonTemplate` | `0x4F2D3A` | Name, Parent, Size, Font, Name, ColorNormal, ColorPressed, ColorHighlit, ColorDisabled, Texture, TextureName, Normal, Pressed, NormalHighlit, PressedHighlit, Disabled |
| `VerticalResizeImageTemplate` | `0x4F3134` | Name, Parent, Width, TopHeight, RepeatHeight, BottomHeight, Texture, TextureName, Top, Repeat, Bottom |
| `HorizontalResizeImageTemplate` | `0x4F336F` | Name, Parent, Height, LeftWidth, RepeatWidth, RightWidth, Texture, TextureName, Left, Repeat, Right |
| `FullResizeImageTemplate` | `0x4F35AA` | Name, Parent, TopHeight, MiddleHeight, BottomHeight, LeftWidth, MiddleWidth, RightWidth, Texture, TextureName, TopLeft, TopMiddle, TopRight, MiddleLeft, MiddleMiddle, MiddleRight, BottomLeft, BottomMiddle, BottomRight |
| `VerticalScrollbarTemplate` | `0x4F3932` | Name, Parent, ThumbVResizeTemplate, UpButtonTemplate, DownButtonTemplate, GutterVResizeTemplate, Width, Height, UpButtonOffset, DownButtonOffset, ThumbOffset, ActiveZoneOffset |
| `MenuTemplate` | `0x4F3C26` | Name, Parent, Width, Height, LabelLength, Font, Name, ColorNormal, ColorHighlit, Texture, TextureName, Normal, Highlit, CheckTexture, CheckTextureName, CheckTextureCoords, CheckTextureSize, CheckOffset, ScrollbarTemplate, MaxVisibleItems |
| `ExpandableMenuTemplate` | `0x4F40A3` | Name, Parent, OpenButtonTemplate, CloseButtonTemplate, TitleButtonTemplate, TitleName, MenuTemplate, MenuOffset, OpenButtonOffset, CloseButtonOffset |
| `ComboBoxTemplate` | `0x4F42DA` | Name, Parent, DownButtonTemplate, LabelButtonTemplate, MenuTemplate, MenuOffset, ButtonOffset |
| `ListBoxHeaderTemplate` | `0x4F4493` | Name, Parent, Texture, Name, Size, Normal, Pressed, Highlit, TextFont, Name, ColorNormal, ColorPressed, ColorHighlit, ColorGreyed |
| `ListBoxTemplate` | `0x4F47EB` | Name, Parent, LinePadding, CellPadding, LabelLength, ScrollbarTemplate, ScrollbarTopOffset, ScrollbarBottomOffset, ScrollbarRightOffset, DrawScroll, Sorting, BackgroundTemplate, TextFont, Name, ColorNormal, ColorHighlit, ColorGreyed, TextLeftOffset, TextTopOffset, TextRightOffset, TextBottomOffset, LineOffset, IconSize, IconTemplate, SelectedRowName, SelectedRowOffset, AlternateRowName, AlternateRowOffset, DisplayAlternateRow, HeaderHorizResizeTemplate, HeaderLeftOffset, HeaderRightOffset, HeaderTopOffset, HeaderHeight, HeaderBackgroundTemplate, HeaderControl, Column, TemplateName |
| `TextAreaTemplate` | `0x4F4FB8` | Name, Parent, ScrollbarTemplate, LinePadding, BackgroundTemplate, Font, Name, ColorNormal, ColorActive, TextOffset, ScrollbarOffset, ScrollbarSizeOffset |
| `StatusBarTemplate` | `0x4F52C7` | Name, Parent, BackgroundHResizeTemplate, ForegroundHResizeTemplate, Height, ForegroundOffset |
| `ImageAreaTemplate` | `0x4F5461` | Name, TopLeft, TextureName, Size |
| `StatusIconTemplate` | `0x4F557E` | Name, TextureName, Width, Height, MaxLevels, Horizontal, TextureStart |
| `IconTemplate` | `0x4F572A` | Name, Size, FontName, FontColor, IconSize |
| `ChatControlTemplate` | `0x4F58B8` | Name, FontName, BackgroundTemplate, ScrollbarTemplate |
| `CheckBoxTemplate` | `0x4F59BA` | Name, Parent, Size, Font, Name, ColorNormal, ColorPressed, ColorHighlit, ColorDisabled, TextOffset, TextAlignment, ChangeFontColor, Texture, TextureName, Normal, Pressed, NormalHighlit, PressedHighlit, Disabled |
| `VerticalStatusBarTemplate` | `0x4F5E4C` | Name, Parent, BackgroundImage, ForegroundImage, Width, ForegroundOffset |
| `CompassControlTemplate` | `0x4F5FE6` | Name, Parent, Texture |
| `TreeControlTemplate` | `0x4F60C3` | Name, BackgroundTemplate, CheckBoxTemplate, IconTemplate, ScrollbarTemplate, FontName, FontColor, LinePadding, OffsetX, OffsetY, TextOffset |
| `IconSetTemplate` | `0x4F632C` | Name, CellPadding, LinePadding, IconTemplate |
| `EditBoxTemplate` | `0x4F6421` | Name, Parent, ScrollbarTemplate, LinePadding, BackgroundTemplate, Font, Name, ColorNormal, ColorActive, TextOffset, ScrollbarOffset, ScrollbarSizeOffset |
| `HorizontalSliderTemplate` | `0x4F66DC` | Name, Parent, BackgroundHResizeTemplate, ImageAreaTemplate, Height |
| `VerticalSliderTemplate` | `0x4F6831` | Name, Parent, BackgroundVResizeTemplate, ImageAreaTemplate, Width |
| `ImagePickerTemplate` | `0x4F6986` | Name, Parent, IndicatorTemplate, CellSize, BorderSize, Width, Height |
| `TabsTemplate` | `0x4F6B1D` | Name, Parent, ControlAreaFullResizeImageTemplate, TabButtonTemplate, TabXOffset, TabYOffset |
| `HorizontalResizeButtonTemplate` | `0x4F6C8B` | Name, Parent, Height, Font, Name, ColorNormal, ColorPressed, ColorHighlit, ColorDisabled, Normal, Pressed, NormalHighlit, PressedHighlit, Disabled |

Stock `ImageAreaTemplate/Parent` and `ListBoxTemplate/ShowDefaultToolTips` have
no compare and are ignored.

## 10. Open points

- The 0x53E365 registration path, the window registration tail
  (`0x4E3D6C`/`0x4E5AAE`) and whether allocations are zero-filled are not
  decoded. "not set" defaults are therefore undefined, not zero.
- `0x4ECA97` chooses between UTF-8→Latin-1 and UTF-8→ANSI on `[0x104B3D8]`;
  when each applies is not traced.
- The ScalarLabelDef `Format` consumer and the TabsDef tab-record layout past
  `Id`/`Name` are not traced.
- The per-window ControlId click dispatch is not traced. The eight unmapped
  stock event names, the unmapped stock WindowId and ControlId names, and the
  ignored stock child elements look harmless (§6), but that conclusion comes
  from the stock XML pattern, not from code or a real-client check.
