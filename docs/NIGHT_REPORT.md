# Nachtlauf 2026-09-28 (Orchestrator: Fable, `/loop`)

## Fortschrittslog (für eine fortgesetzte Session: hier weitermachen)

Start 03:14 CEST. Stand bei Start: `main` = 0.117.0 (a04e02a), Install läuft
0.115.0 (Build 2026-09-27 23:52), Server läuft, kein Spieler eingeloggt.
Worktrees liegen unter `/mnt/d/OfflineDAoC-wt/<paket>`, Branches `night/<paket>`.

| Paket | Typ / Agent | Worktree | Status |
|---|---|---|---|
| Ked Co-Leader (GuildRank 1) | Orchestrator, beim ersten `server.sh update --no-start` | – | ✅ 04:18: Backup `playable-backups/opendaoc.pre-ked-rank-20260928-041813.db`, UPDATE traf genau 1 Zeile, Ked GuildRank 9 → 1, integrity ok. Batch 1 (0.122.0) Build+Tests grün (Server 2368, Launcher 5 bekannte Fehler = Bug 59); 1. Deploy-Versuch 04:10 scheiterte: Launcher-Fenster minimiert, UI-Automation fand STOP nicht → `server.sh` stellt Fenster jetzt vorher wieder her (`restore-window.ps1`); 2. Versuch 04:16 erfolgreich: 17 Dateien, Backup `deploy-20260928-041631`; Start 04:19 läuft |
| Bug 56 BotBrain-Ticks langsam | daoc-developer (Opus), erst profilen | `bug56` | entwickelt 04:00 (Profil per dotnet-stack: 48 % Navmesh-Korridorprüfungen, 21 % Koordinator-Lock; Seitenschritt-Suche in 8-ms-Scheiben, Stall-Netz-Cache 30 min, Profiler `BOT_THINK_PROFILE`; 2340 Tests grün), Review ACCEPT 04:10 → ✅ gemergt als 0.120.0 |
| Bug 57 `/tc` doppelt | daoc-bugfixer (Sonnet) | `bug57` | ✅ gemergt als 0.118.0 (ae5adf5), noch nicht deployt/gepusht |
| Bug 58 Dashboard-Snapshot (+ Bug 26 Weltgeschwindigkeits-Statusdatei, gleiche Ursache) | daoc-bugfixer (Sonnet) | `bug58` | 58+26 entwickelt 03:40 (gemeinsamer Retry-Helfer, Launcher-Leser mit FileShare.Delete, 2331 Server-Tests grün); Runde 2 fertig 04:12, Nach-Review ACCEPT 04:23 → ✅ gemergt als 0.122.0 |
| Log-Check Bugs 20/25/28/29/30/33 | general-purpose (Sonnet), read-only | – | ✅ fertig; BUGS.md aktualisiert 04:27: 25/28/30/33 → Finished, 20 bleibt pending, 29 gefixt; neu 59 (Launcher-Tests, ex-33), 60–63 |
| Bug 29 Getrennte Gruppen warten auf Rez | daoc-bugfixer (Opus) | `bug29` | entwickelt 03:40 (Phase-Überschreiben, Rescuer-Radius, Drop nach 2 Rejoin-Toden; 2333 Tests grün), Review ACCEPT 03:50, Nachbesserungen erledigt → ✅ gemergt als 0.119.0 |
| Task 47 Advisor (Bots leveln kaum) | daoc-advisor (Opus) | – | fertig 03:27, Bericht docs/night/ADVISOR_47.md |
| Task 48 Advisor (echtes RvR) | daoc-advisor (Opus) | – | fertig 03:30, Bericht docs/night/ADVISOR_48.md |
| Task 46 Pet-Pull als Gruppenmodus | daoc-developer (Opus) | `task46` | entwickelt 04:40 (Modus pro Spieler, Pull startet mit Pet-Angriff, Gefahr-Reaktion Heiler+Tank-Taunt, 16 Tests, 2350 grün), Review ACCEPT 04:20; Nacharbeiten fertig (2352 Tests grün) → ✅ gemergt als 0.125.0 (04:27) |
| Task 47 Build A (Con-Erholung, lokale Solo-Camps, Todes-Logzeile) | daoc-developer (Opus) | `task47` | entwickelt 03:45; Review REJECT 03:55 (Runde 1/2: Camp-Wechsel nach Erholung landet wieder auf Grün, Kreuzungs-Cache pro Region ignoriert Dungeon-Eingänge) ; Runde 2 fertig 04:14 (Umzug nur zu härterem Camp in ≤20 min, Con-Gewicht Blau/Gelb ×2, Kreuzung pro Zelle; 2349 Tests grün), Nach-Review ACCEPT 04:20 → ✅ gemergt als 0.121.0 |
| Task 47 Build C (Gruppen-Taskuhr ab Camp-Ankunft, Camps nahe Treffpunkt, keine Umplanung unterwegs) + RvR-Ablaufzeit bei pausierter Uhr (Advisor 48 Ursache e) | daoc-developer (Opus) | `task47c` | entwickelt 04:32 (Befund: Uhr startete schon am Camp, keine Umplanung unterwegs → Advisor-Prämissen D1/D3 falsch; echte Verluste: 32 Reisefenster-Timeouts, davon ~13 Dungeon-Gruppen <2.500 Einheiten vor dem Camp → starten jetzt ihren Task; Camps nahe Treffpunkt gewichtet; RvR-Ablaufzeit bei pausierter Uhr; 14 Tests, 2349 grün), Review ACCEPT 04:17; Nacharbeiten fertig → ✅ gemergt als 0.124.0 (04:24) |
| Task 47 Build B (Kollateral-PvP in BotBrain) | daoc-developer | – | wartet auf Live-Daten der Todes-Logzeile aus Paket A |
| Task 48 Build (Hubs sicher, Portal-Keep-Türen, Aufgeben nach 3 Fehlversuchen, gemeinsam porten, Release-Verhalten) | daoc-developer (Opus) | `task48` | entwickelt 04:05 (39 neue Tests, 2369 grün; Hubs 3.500 Einheiten sicher, Türregel = KeepManager.IsEnemy, Aufgeben nach 3 Fehlversuchen, Leerlauf-Belagerung nach 15 min zu, 60 s gemeinsam porten, Release am eigenen Hub); Review REJECT 04:22 (Runde 1/2: Leerlauf-Schließung trifft noch marschierende Truppen, Aufgeben gilt pro Bot statt pro Truppe); Runde 2 fertig 04:30 (Marschfortschritt hält Belagerung offen, Aufgeben pro Truppe im Event-Layer; 2373 Tests grün), Nach-Review ACCEPT, Nacharbeit (Marsch-Gutschrift gedeckelt) fertig → ✅ gemergt als 0.123.0 (04:16) |
| Bug 59 Launcher-Tests (ex-33) + umgebungsabhängiger Ratentest | daoc-bugfixer (Sonnet) | `bug59` | ✅ gefixt (Fixture mit 5 Konstruktorargumenten, Testseam für Prozess-/Portcheck; 127/127 grün) → gemergt als 0.123.1 |
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
- Launcher-Testsuite auf diesem PC: 4 Fehler = Bug 33 (steht in Finished, tritt aber weiter auf → zurück nach Open), 1 Fehler `PlayerAndBotRatesPersistIndependently` nur, weil der echte Server läuft (Prozesserkennung); für die Nacht kein Blocker, aber notieren.
- Bug 56 (Profil): 81 % der langsamen Ticks kommen von Weltbots im Planungsmodus; ein festgefahrener Bot (Leofismund) blockierte 53 min lang mit ~160 ms pro Runde den ganzen NPC-Service. Reaper-Hänger: 875 von 876 sind Kills von Ked in DF (Ø 425 ms), Nova Ø 1,4 s → Verdacht synchrone SQLite-Schreibzugriffe (Companion-Gear-Belohnung) auf der 5.400-rpm-HDD D: (Median 253 ms pro langsamer SQL-Anweisung). **Entscheidung für Aaron:** Spielstand auf die SSD umziehen? Koordinator-Lock-Umbau und Stallplaner-Slicing bleiben offen bis der Minuten-Log sie bestätigt.
- Task 48 (Build): offene Frage für Aaron: Der Landepunkt des Allrealm-Teleporters bei Castle Sauvage (583913,487012) und die Bindsteine dort liegen ~9.000 Einheiten vom Hub-Zentrum, also außerhalb des 3.500er-Schutzradius (Svasud ~4.000). Der Entwickler hat den Radius ohne Kartenbeleg nicht vergrößert → Real-Client-Blick nötig. Menschen im Hub sind jetzt ebenfalls unangreifbar (Entscheidung 7).

## Zusammenfassung für Aaron

- (wird am Ende geschrieben)
