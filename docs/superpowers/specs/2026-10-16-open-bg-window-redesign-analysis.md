# OPEN BG window redesign: what the client UI allows, concepts, plan (advisor, 2026-10-16)

Read-only analysis (opus advisor). Mock renderer and PNGs: `docs/open-bg-redesign/` (`current_lage.png` = today, `concept_lage.png` and `concept_setup.png` = concept A; `concept.py`, `concept_labels.py` are the scratch tools of the concept (the renderer is now `source/server/tools/render_open_bg_mock.py`), they read the client's TGA fonts/textures and need Pillow). Owner request: high-end look, fun, simple and understandable, show group members individually, always tidy, spacing is ugly now. Hand-off: `docs/superpowers/plans/HANDOFF-open-bg-window-design.md`.

## 1. What the technology allows

**Without any game.dll change (XML, label texts, server):**
- Realm crests 64x64 (`alb_symbol`, `mid_symbol`, `hib_symbol`, texture `isles/realm_symb.tga`; templates in `ui/atlantis/realmwar_{alb,mid,hib}.xml`, registered in `realmwar_key.xml`); 16 px map icons (`isles/map_icons.tga`: keep/tower per realm colour, realm badges tree/hammer/chalice at 9, 13, 16 px): static, not data driven.
- Own templates on stock textures (e.g. the gold-framed leather bar from `frames_win` (10,61) 24 px; precedent `community_window.xml` `listbox_row_bg`, `realmwar_key.xml`); the builder test `test_companion_manager_client.py:719` enforces exactly 2 templates today and must be loosened. A custom TGA texture is possible (`<Texture>` in window files) but needs installer changes (all stock textures are 2^n, max 512x512 at 32 bpp; larger unverified).
- Frames/panels: `inner_frame`, `dlg_interior_small/large`, `dlg_background_resize`, `tool_tip_background`, `selection_box`, `menu_background_noresize` (`atlantis/styles.xml`). Dividers/rows: `context_hr_image_divider` (1 px), `listbox_row_bg` / `listbox_selected_bg` (16 px), `listbox_header`, `dialog_inset`, `tab_button_normal/_highlit`. Zebra rows work because rows have fixed positions. Colour strips `green/red/yellow/blue_full/_back` (6 or 3 px, any width, fixed width only: decoration).
- Hover highlight: `HorizontalResizeButtonDef` with `context_hr_button` / `simple_hr_button` (NormalHighlit image only); arrow buttons `button_up/down/left/right`, `scroll_up/down` have Normal/Pressed/Highlit. UNVERIFIED: whether clicks with a decimal event (0xA00+n) from this button type reach the BG handler (proven only for `InvisibleButtonDef`).
- Fonts: `arial9/11/14`, `title` (gold engraved serif, colour baked in, ~24 px), `barb10`, `brit9`, `button_small/large`; TTF `chat_small/large`, `myriadbold`, `minion` (`atlantis/assets.xml`). `title` with an adapter is stock (`map_window.xml:196`) = a server-driven gold line. `Arial18.tga` and `minionpro-bold.ttf` exist but are not registered; own registration in a window file unverified.
- Alignment: `TextCentered`, `EndAligned` (right aligned, 28 stock uses).
- Text bars with any value: two overlaid labels, grey `"|"` x40 under green `"|"` x n (`"|"` = 2 px resolution); pips with `"="` (8 px each). Server only; real glyph spacing unverified.
- Collapse/expand by click: the server remembers state and re-renders (pattern: Companion Manager 0.169.0 sections `[-]/[+]`, `COMPANION_MANAGER_INTEGRATION.md:124`). No native part.
- Free click controls: `0x16-0x1F`, `0x36-0x3F`, `0x56-0xDF`, `0xEC-0xFC`; native range `0xA00-0xAFF`; `TryParseBgControl` accepts 00-ff.

**Hard limits:** single controls cannot be shown/hidden (empty labels draw nothing, images always draw); one fixed colour per label (each colour variant = an overlaid label = one adapter); server text is ASCII only, max 115 characters (`CompanionManagerProtocol.cs:18,289,321-347`; fixed XML text may use cp1252: Arial14.tga has bullet, guillemet, middle dot, times, em dash, plus-minus); no gradients, round shapes, shadows (except baked into a texture), no data animation (`FrameAnimDef`/`StagedStatusBarDef` parse but are never stock); no hover event, therefore no server hover help (`ToolTipID` points to unknown client strings); no right/double click, no edit focus; no class icons in the client (`app/icons` has only `itm*`, `spl*`, `skl*`, `cbt*`, `wg*`): class stays text; the client does not report window size; stock `group_*` adapters only show the player's own group; Isles skin does not register `page1-3` and `frames_win` (grep): affects today's window too (`dlg_sm_title_noresize` lies on `page3`), unverified.

**Needs native game.dll change:** real bars with live values (HP per bot, phase clock) and `StatusIconDef` (keep colour via `realmwar_keep`): new op "set scalar n" + scalar adapters, pattern gear window `cmgr_icon_<n>` (builder:84-85), 1-1.5 days, risk: `StatusBarDef` with own scalar unproven; more than 985 labels: `TOTAL_LABEL_COUNT` is a parameter (builder:362, 546-582), 0.5 day + installer hashes; dynamic label colour: colour adapter has a registrar but no setter, 1-2 days, not recommended; reporting window size: lever `0x53DCAA`, not needed.

## 2. Today, measured
Live snapshot `playable/runtime/server/open-bg-status.json`: 60 bots, 30 units (20 solos), unit sizes 1-6, Alb 24 / Mid 17 / Hib 19, archetypes 42 empty / 12 caster8 / 6 std8, roles roam 30 / lurk 15 / bridge_watch 12. **Leader is empty for 60 of 60 bots (bug):** `OpenBattlegroundStatus.cs:161` reads `bot.Group?.Leader?.Name`, but `Group.Leader` is `LivingLeader as GamePlayer` (`Group.cs:59-61`), null for a bot leader. Fix: use `LivingLeader`. Lage page today: 67 rows = 4 pages of 22, five dense header lines, empty Value column, Unit repeated in every bot row, no unit header row.

## 3. Concepts
Tokens (existing values `build_companion_manager_client.py:213-221`): Ink `#E1E1E1` text; Gold `#FFD25A` title and selection only; Ash `#969696` secondary; Field `#78D782` alive/on; Blood `#E66E64` dead/limit (new); Dim `#464646` bar ground (new); Realm Alb `#DC7D78`, Mid `#87A5EB`, Hib `#7DC887` only for names and crests.

**A "Kriegstafel" (recommended), 760x640.** A 2003 command table: one glance shows who stands where. Bold only in the triptych: three 64 px crests with live numbers, under it the leather bar with "Keep 11: Albion holds it" in the gold `title` font. Everything else quiet arial14. Wireframe (Lage; columns x: A=12 B=28/40 C=160 D=190 E=292 F=418 G=504 H=650/700):
```
  0 OPEN BG  Thidranki | on and ready | 1 player inside               [X]
 24 +-inner_frame 744x100-------------------------------------------+
 40 |[ALB 64] Albion  24 crew |[MID 64] Midgard 17 crew |[HIB 64] Hibernia 19
 60 |         11 units, plan 10|        10 units ...    |        ...
 82 |         |||||||||||  22in|        ||||||||  14in  |   ||||||  12in   (text bars 110 px)
130       [==== banner 500 px: "Keep 11: Albion holds it" (title) ====]
158 Quiet | next push in 35 min | field 12 of 27 units out (72%) | speed 1x
178 ---------------------------------------------------------------------
184 [Overview][ Setup ]  Rows 1-16 of 16  [Open all][Fold all]  ^ v  [Refresh][Close]
208   Unit / name     Lv  Class      Doing          Strength   Note
226 ---------------------------------------------------------------------
232   ALBION          11 units, 24 crew  22 inside                  (zebra every 2nd row)
252 + caster8 #31     6 bots         assault        ======     Raid
272 - small #32       3 bots         roam           ===                   [Relieve]
292 *   Aelelallyn    50  Cabalist   in the field   12 min
312     Ranenren      50  Theurgist  dead
332 + Solo hunters    6 of 6         lurk           ======
 .. (18 rows, pitch 20, to y=592)
600 + opens a unit, - folds it. * marks the leader. Click a crest to jump.   (hint line)
618 Bot cap: 68 -> 72.                                                       (reply)
```
Steuern (same header; columns B name | C area | E value | F measure bar | H [-] [+] / link): sections Battleground (Open BG on/off `[Turn off]`, Mode `[Use Per realm]`, Landing peace `[Next: All]`), Crew (Bot cap 8-120 step 4 with `|` bar and `[-] [+]`, 8-man per realm 0-3), Level band, Presets (std8 2 `==` `[-][+]`), Timing; hint line explains the selected row (">" gold marker, a click on the name selects it). Row slots (18): 0 mark gold `+ - * >` (x12 w14); 1 title gold (x28 w460); 2 text Ink (x28 w700); 3-5 name in three realm colours (x40 w116); 6 level (x160 w24); 7 class (x190 w98); 8 Ash (x160 w128); 9-11 state Field/Blood/Ash (x292 w122); 12-13 bar Dim/Field (x418 w80); 14 note gold (x504 w140); 15 link (x650 w96); 16 `[-]` (x650); 17 `[+]` (x700).

**B "Feldbericht" 560x500:** text-driven dispatch, no crests; the memorable element is the battle clock (a `title` line "Quiet - push in 35 min" plus a 536 px `|` bar); realm only as coloured abbreviations ALB/MID/HIB; members as one line per unit with class abbreviations (worse readability). Budget 300.

**C "Heerschau" 980x640:** three realm columns with crest heads; needs 528 labels > 365 (native label expansion) and 980 px collides with the manager window width; columns only 316 px.

## 4. Groups shown individually (concept A)
Hierarchy realm row (name in realm colour, units, crew) > unit header (mark `+`/`-`, gold title "caster8 #31" or "8-man #12"/"small #23", Ash "6 bots", coloured order, strength pips, note "Raid" in gold) > members (leader first with gold `*`; name indented in realm colour; level, class; state "in the field" Field, "dead" Blood, "travelling/outside/leaving" Ash; time inside Ash). Solos grouped per realm as "Solo hunters 6 of 6". Default: everything folded = 3 realm rows + 10 group units + 3 solo blocks = 16 rows = one page (today 67 rows). Fold by clicking the unit header (new controls `0x60+r`), plus `[Open all]`/`[Fold all]` (`0xEC`/`0xED`); the session remembers opened unit ids; new units start folded; server-side only. Crest click jumps to the realm section (`0xF0-0xF2`). Relieve only at an opened unit header with a second click within 10 s (the Relieve list on Setup goes away). Leader markers need the `LivingLeader` fix first.

## 5. Setup redesign
Gold section rows: Battleground (On/Off, Mode, Landing peace), Crew (Bot cap, 8-man/Small/Solos per realm), Level band, Group presets, Timing (player drift, shortest/longest cycle), with Per realm also "Units per realm". Switches: value in Field "On" or Ash "Off", link names the result "[Turn off]"; Mode "Server PvP" with "[Use Per realm]"; Landing peace "Auto" with "[Next: All]". Steppers `[-]`/`[+]` at fixed positions, at the limit the step stays empty and the reply explains why; range in Ash ("8-120, step 4"), value as `|` bar. Help: no hover, so a click on the name selects the row (gold `>`) and the hint line explains it in one sentence. Language: English everywhere (server, chat, client are English; "Lage/Steuern" next to "Mode: Server PvP" is mixed today): tabs "Overview" and "Setup".

## 6. Copy
Verbs: Open, Fold, Relieve, Turn on/off, Use <mode>, Next: <value>, Refresh, Close. Empty states: "No field report yet. It updates every 15 s; click Refresh." / "The open BG is off. Turn it on in Setup." / "Not ready: <reason>. The crew waits until the zone is ready." / "No crew yet. The roster forms units within a game minute (levels 44-50)." (+ "Last try 2 min ago: <reason>" with FormFailures). Replies: "Bot cap: 68 -> 72." / "Bot cap is at its maximum (120)." / "Relieve caster8 #31? Click again within 10 s." / "caster8 #31 relieved; a fresh unit forms within a game minute." / "The view changed; nothing was done. Try again."

## 7. Budget and size
Concept A: 18 rows x 18 slots = 324 + 31 fixed labels (status, 12 realm fields + 3 counters, banner, phase, 4 tabs, page, 6 column heads, hint, reply) = 355 of 365; game.dll unchanged (985 total). Change `BgRowCount`/`BgRowStride` (22/16 -> 18/18) in the builder and `CompanionManagerProtocol.cs:202-203` (a test compares both). Size 760x640 instead of 648; making it resizable is pointless (server does not know the size); a compact mode is not worth it (folded fits one page). B fits the budget, C does not.

## 8. Recommendation and plan
Concept A (stock assets only, no native risk, a clear signature (crests and banner), one page). Tasks: (1) server data: leader via `LivingLeader` (`OpenBattlegroundStatus.cs:161`), a solo is its own leader; (2) builder: new `open_bg_window()` / `open_bg_row_slots()` with own templates (`obg_banner`) before the `WindowTemplate`, crests, `title` banner, zebra, tab plates; hover buttons first only for tabs and arrows, rows keep `InvisibleButtonDef` until the click path is proven; loosen the template test (`:719`: own templates allowed, only on stock textures); (3) server screen: unit blocks, fold set, selection and hint, crest jump, Relieve with two clicks, new control bases, English texts, tests; (4) preview into the repo: `render_open_bg_mock.py` (system python3 has Pillow 12.3; TGAs uncompressed; Arial14 and `title` cut by their marker lines, 224 glyphs): sample snapshot to PNGs; feasible and proven; (5) stage 0.283.0: new `custom5` hashes, installer accepts 0.282.0's `custom5` as start, release pack, docs, CHANGELOG (MINOR), TASKS pending, real-client checklist, the owner reinstalls.
Risks: no real render (the mock knows no hover, no `StatusBarDef`, no `IconDef`; tiles and glyph spacing approximated); `title` in the 24 px bar; hover clicks with own event; Isles textures; only 10 free labels left.

## 9. Open questions with proposals
1. Language: English, tabs "Overview / Setup" (proposal) or German? 2. Units on open: folded (proposal) or open? 3. Relieve: at the opened unit header with two clicks (proposal) or only in Setup? 4. Real HP bars per bot (native, about 1.5 days): later (proposal) or now? 5. Skin: Atlantis or Isles (decides how much Isles must be verified)?

## Not verified
Hover and clicks of `HorizontalResizeButtonDef` with decimal event; label alpha; own `Font`/`TTFFont` registration in a window file; cp1252 bytes via adapters; origin of `ToolTipID` texts; `FrameAnimDef`; textures larger than 512x512; texture registration in the Isles skin; real spacing of the `|`/`=` bars.

## Built (0.283.0)

Concept A was built as "Kriegstafel" (see `docs/COMPANION_MANAGER_INTEGRATION.md`, section OPEN BG window redesign). Offline only; the real-client check is pending. Deviations from the concept:
- Steppers `[-]` `[+]` sit at x 502 and 546 right after the bar (not at 650/700: their click areas would overlap the link column). Name slots start at the title x (28, width 128); members get two leading spaces for the indent (unproven in the client; fallback names at x 28).
- `[Refresh]` and `[Close]` are at the bottom right (the tab line had no room; the title bar must stay free of click areas).
- No hover buttons: tabs, arrows and links are invisible click areas (decimal-event hover buttons are unproven). Both tab plates are the same image; only the label (gold capitals vs Ash) shows the active tab.
- The per-realm mode link reads `[Use PvP]` (`[Use Server PvP]` does not fit the 108 px link slot). `[Open all]` and `[Fold all]` are server labels (Overview only). Budget 357 of 365 labels, not 355.
- The live crew needs two pages: a folded Overview of the live roster has 26 rows (15 group units), more than the 16 of the concept; Setup is two pages as well.
- Solo hunters are one block and cannot be relieved from the window (a block covers several units); `/bgsetup relieve <unit>` still can.
- Unit titles use the Ink slot, not gold (gold only for banner, section titles, marks and the active tab).
- Banner clip: the keep line is cut by the C# `TitleAdvances` table (measured from `title_24.tga`) to 468 px; the space advance of 7 px is an estimate.
