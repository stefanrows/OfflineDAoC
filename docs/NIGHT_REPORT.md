# Nachtlauf 2026-09-28 (Orchestrator: Fable, `/loop`)

## Fortschrittslog (für eine fortgesetzte Session: hier weitermachen)

Start 03:14 CEST. Stand bei Start: `main` = 0.117.0 (a04e02a), Install läuft
0.115.0 (Build 2026-09-27 23:52), Server läuft, kein Spieler eingeloggt.
Worktrees liegen unter `/mnt/d/OfflineDAoC-wt/<paket>`, Branches `night/<paket>`.

| Paket | Typ / Agent | Worktree | Status |
|---|---|---|---|
| Ked Co-Leader (GuildRank 1) | Orchestrator, beim ersten `server.sh update --no-start` | – | offen |
| Bug 56 BotBrain-Ticks langsam | daoc-developer (Opus), erst profilen | `bug56` | gestartet |
| Bug 57 `/tc` doppelt | daoc-bugfixer (Sonnet) | `bug57` | ACCEPT 03:30 → wird als 0.118.0 in `main` gemergt |
| Bug 58 Dashboard-Snapshot (+ Bug 26 Weltgeschwindigkeits-Statusdatei, gleiche Ursache) | daoc-bugfixer (Sonnet) | `bug58` | 58 entwickelt 03:29 (Retry um File.Move, Test, 2330 grün); 26 nachbeauftragt (Server-Retry + Launcher-Leser FileShare.Delete) |
| Log-Check Bugs 20/25/28/29/30/33 | general-purpose (Sonnet), read-only | – | fertig 03:25: 25/28/30/33 bewiesen behoben, 20 seltener (24 von 164 Gruppen laufen ins 30-min-Limit), 29 tritt weiter auf |
| Bug 29 Getrennte Gruppen warten auf Rez | daoc-bugfixer (Opus) | `bug29` | gestartet |
| Task 47 Advisor (Bots leveln kaum) | daoc-advisor (Opus) | – | fertig 03:27, Bericht docs/night/ADVISOR_47.md |
| Task 48 Advisor (echtes RvR) | daoc-advisor (Opus) | – | fertig 03:30, Bericht docs/night/ADVISOR_48.md |
| Task 46 Pet-Pull als Gruppenmodus | daoc-developer (Opus) | `task46` | wartet auf 56 (beide BotBrain.cs) |
| Task 47 Build A (Con-Erholung, lokale Solo-Camps, Todes-Logzeile) | daoc-developer (Opus) | `task47` | gestartet 03:32 |
| Task 47 Build B (Gruppen-Taskuhr ab Camp-Ankunft) | daoc-developer | – | wartet auf Merge von Bug 29 (Coordinator) |
| Task 47 Build C (Kollateral-PvP in BotBrain) | daoc-developer | – | wartet auf 56 und auf die Todes-Logzeile |
| Task 48 Build (Hubs sicher, Portal-Keep-Türen, Aufgeben nach 3 Fehlversuchen, gemeinsam porten, Release-Verhalten) | daoc-developer (Opus) | `task48` | Worktree wird angelegt |
| Task 45 Lastcheck | Orchestrator, Live-Server | – | offen |

Regeln aus dem Brief: pro fertigem Bug/Task ein MINOR-Bump, Pins in Gleichschritt,
Build + volle Tests vor Merge, `server.sh update`, ~15 min beobachten, dann Push.
Bei Budgetwarnung: WIP auf den Worktree-Branches committen, `main` grün lassen,
Log aktualisieren, Wakeup nach dem Reset planen.

## Gelernt / Entscheidungen

- Log-Check (Lauf 0.115.0, 23:54–03:20, 3 h 26 min): keine NullReferenceException im ganzen Lauf; einzige Exceptions sind Bug 57 (`&tc`) und Bug 58 (Statusdatei). 352× „Couldn't find a zone" nur als WARN → Bug 28 behoben. 8er-Gruppen bilden sich (28× assembled, 13× formed) → Bugs 25/30 behoben. Alle 38 Snapshots mit Region 249 haben Camp in DF → Bug 33 behoben. Bug 20: 24 Camp-Timeouts vs. 140 gestartete Tasks (85 % Erfolg) → bleibt offen, gemildert. Bug 29: Gruppe `1e7771ca…-166` hielt 1 h 43 min „Waiting for resurrection" (75 Holds, 40 Rez-Timeouts) für ein Mitglied in Region 100, Leader in Region 1; neun weitere Gruppen gleiches Muster → wieder offen, Fix gestartet (Coordinator ~Z. 1985–2020 und ~2451–2484: Rez-Hold ohne Distanz-/Regionsgrenze, Kampfstatus aus der ganzen Gruppe).
- Neue Auffälligkeiten im Log (nicht schwer, für Aaron notieren): `REALM_RAID_HUB_ROUTE_FAILED event=epic-albion` ~175×, `Ability 'ConfusionImmunity' unknown` 939×, `LineXSpell Spell Adding Error` 240×, `SortStyles NULL style`/„Unhandled spell Bladeturn" ~1.967×, `RVR_KEEP_ROUTE_FAILED rvr-keep-75 Region 100` (Advisor 48 zählt).

- Task 47 (Advisor): Bots leveln ~0,5 Level/Bot-Stunde, keiner über 32; nur 22 % der Zeit im Kampf, 6 % am Camp. Hauptursache bewiesen: die Solo-Con-Obergrenze sinkt nach jedem PvE-Tod und erholt sich nur beim Neustart → 323 von 462 Bots jagen Grün (25–45 % XP). Dazu: Solo-Ziele ab 20 in fremden Regionen (Hälfte stirbt unterwegs), Gruppen erreichen nur 12 % ihrer Camps, ~4.700 PvP-Tode von Levelern in 3,4 h fast alle „kollateral". Task 7 ist eine andere Ursache (1.65-Kurve, Wand ab 40) → wird als erwartetes Verhalten dokumentiert. Entscheidung: XP-Rate nicht anheben; Plan in 3 Paketen (A jetzt, B nach Bug 29, C nach Bug 56).
- Task 48 (Advisor): von 366 RvR-Bots sind nur 50 lebend im Frontier-Feld, fast alle solo; 76 stecken in den gegnerischen Portal-Keeps in Odin's Gate fest (Routenplaner hält Portal-Keep-Türen für feindlich, Realm=0), 110 stehen an den Grenz-Hubs, wo 82 % aller Tode passieren, weil die Hubs entgegen CAMLANN-Entscheidung 7 nicht als sicher gelten. 0 Keep-Eroberungen je, 318 von 320 Frontier-Teleports solo. Entscheidung: Build nach Advisor-Plan 1–5 (sichere Hubs, Türenlogik, Aufgeben, gemeinsam porten, Release-Verhalten); Punkt 6 (1.65-Feinschliff) erst, wenn Gruppen überhaupt draußen sind.
- Werkzeug-Hinweis: `playable-dev/dbquery.py` scheitert bei laufendem Server am Live-WAL mit „disk I/O error" und überschreibt dabei den Snapshot; die Agenten haben mit Lesekopien im Scratchpad gearbeitet. Für Aaron notieren (Werkzeug außerhalb des Repos).

## Zusammenfassung für Aaron

- (wird am Ende geschrieben)
