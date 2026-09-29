# Nacht/Tag 2026-09-29 (Orchestrator: Fable, `/loop`) — RvR-Bots nach 2003-Vorbild

**TL;DR**
- **10 Verhaltenspakete 0.146.0 → 0.157.0, alle live: RvR-Tode/h 1.763 → 790, Hotspot-Konzentration 54 % → 13 %, Wiedersterben 73 % → 42 %.**
- **Jedes Paket folgt einem belegten Prinzip aus 319 Forum-Zitaten (Wissensbasis auf E:, nicht im Repo).**
- **Offen: Speed-Song nur 6–12 % der Reisezeit aktiv (Task 69 pending), Launcher-Dialog (Bug 70).**

| Version | Verhalten | Prinzip |
|---|---|---|
| 0.146.0 | Hub-Fächer, Aufbruchsfrieden, Routen Straße/Flanke/Deckung, Logzeilen RVR_ROUTE_CHOSEN/ROAM_PICK/RETREAT | P3 erst raus, dann jagen |
| 0.147.0 | Gefahren-Gedächtnis 60 min (Vorsichtige meiden, Mutige kommen größer zurück), Rast 1–5 min abseits der Straße, Rückzug zu passierbarem Keep/Hub; Routen-Fix (Bodensuche ±4.096) | P5, P6, P7 |
| 0.148.0 | Beobachten statt Reinrennen: Halt außerhalb 2.200, Add bei 3/2/1 von 8 am Boden, Nachzügler, Push bei CC, sonst gehen | P2, P4 |
| 0.149.0 | Heiler je Spec: Smite/Natur nur mit zweitem Heiler, Pac/Höhle CC vor Heilung, Bomb-Stun, kein Barden-Nahkampf | P1, P8 |
| 0.150.0 | Hub-Frieden in der Angriffsregel (Eigenrealm-Bots im 6-km-Band greifen sich nicht an) | P3 |
| 0.151.0 | Stealther lauern getarnt neben der Straße, weiche Ziele, Absetzen; Assist in 1–2 s; Interrupt-Gewicht; kein DoT/AoE auf Gemezzte | P4, P9–P11 |
| 0.152.0 | PvE: Pets ziehen Orange, Caster rasten bis 75 % Power, Mez-Gruppe vs. Pet-Massenpull, Camp-Wechsel bei Rivale/Wipe | P12, P13 |
| 0.156.0 | Aufbruchsfrieden 8 min, Band 7,5 km (Bandrand-Fleischwolf 6,3 km vor Sauvage) | P3 |
| 0.157.0 | Speed-Klasse bevorzugt (Heiler zuerst), Speed unterwegs, Leader wartet, Sprint schließt Lücken, nie Schritttempo; Tempo-Bugs (Laufbefehl, Low-HP-Bremse) | P6 |

Messfenster je 1 h (`E:\daoc-knowledge\tools\rvr_metrics.py`): Baseline 0.139 21:40–00:40, 0.146.0 11:40–12:30, 0.152.0 14:45–15:45, 0.157.0 17:00–18:00. Details, Baseline, Killer-Analyse und Wellenprotokoll auf E:.

Gelernt: Der 0.146.0-Aufbruchsfrieden (nur Erstangriff) half nicht (Svasud 35 % → 45 %); erst der Frieden in der Angriffserlaubnis (Assist, Flächenschaden, „Vergeltung") brach den Fleischwolf, der dann zum Bandrand wanderte (0.156.0). Routen fielen zu 90 % zurück, weil die Bodensuche das Frontier-Relief nicht fand. Zwei Tempo-Bugs erklären das „Schritttempo": Laufbefehle behielten ihr Starttempo, und die NPC-Verlangsamung unter 33 % HP traf spielerförmige Bots.

Nicht geprüft: echter Client (Fächer-Abgang, Rast abseits der Straße, Beobachten am Hügel, Stealth-Opener, Speed-Song). Launcher-Testprojekt lief nicht (Linux-SDK). Bug 70 (Launcher-Dialog) offen.

# Nachtlauf 2026-09-28 (Orchestrator: Fable, `/loop`)

## Fortschrittslog (für eine fortgesetzte Session: hier weitermachen)

Start 03:14 CEST, Ende 05:00 CEST. **Endstand: `main` = 0.125.0 gepusht, Install
läuft 0.125.0 (Start 04:41), Server läuft, Ked GuildRank 1.** Alle Worktrees und
`night/*`-Branches sind entfernt; nichts ist halb gemergt. (Zeitangaben in der
Tabelle sind teils ~20 min zu spät notiert; die Reihenfolge stimmt.)
Stand bei Start: `main` = 0.117.0 (a04e02a), Install 0.115.0, kein Spieler eingeloggt.

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
- Live-Messung 0.122.0 (Start 04:21, erste 5 min, Population noch im Aufbau): 0 Exceptions, kein `&tc`-Fehler mehr (Bug 57 ✅ live), keine Statusdatei-Fehler (58/26). Lange BotBrain-Ticks ~17/min (vorher ~85/min), Median 37 ms (vorher 94–122), Max 280 ms (vorher 3.967); NpcService Ø 3,2–3,7 ms (vorher 12–15), p95 15 ms (vorher ~60). `BOT_THINK_PROFILE`: RouteRecoverySearch max 15 ms (Ziel <100 ✅). Verbleibende Hotspots: NavPathQuery in TravelAcrossRegions (167 ms/146 Abfragen in einer Runde) und ZoneItineraryStep (219 ms/96 Abfragen) → Folgearbeit für Bug 56. Con-Erholung: erste `AUTONOMOUS_CON_RECOVERY reason=new-task`; 13 Tode geloggt (11 PvE, 2 PvP), Con-Grenzen Blau/Gelb (nach Neustart erwartbar). Bug 29: 0 RESURRECTION_WAIT-Zeilen bisher.
- Live-Messung 0.122.0 nach 17 min mit voller Population (1.210 Bots, 04:21–04:38): 0 Exceptions, 0 Statusdatei-Fehler, 0 lange Reaper-Ticks (aber auch keine Spieler-Kills). Lange BotBrain-Ticks 22–39/min (vorher ~85), Median 41 ms (vorher 94–122), p95 257 ms, Max 1.699 ms (ein Ausreißer; vorher 3.967). NpcService Ø 5,9 ms (vorher 12–15), p95 17 ms (vorher ~60) → Bug 56 live etwa 2,5–3× besser, Rest siehe Hotspots. Con-Erholung: 23 Schritte (16 neue Aufgabe, 5 Level-up, 2 Kills), 7 Camp-Wechsel zu härterer Beute. Tode: 698 in 17 min, davon 495 PvP; bot_was_target=True bei 406, False (kollateral) bei 89, area=True 69 → die „fast alles kollateral"-Hypothese des Advisors trifft so nicht zu; die meisten PvP-Tode sind gezielte Angriffe (Paket B neu bewerten: eher Hub-Gemetzel als Flächenzauber; Task 48 macht die Hubs sicher). Bug 29: keine RESURRECTION_WAIT-Schleifen. RVR_KEEP_ROUTE_FAILED 49 in 17 min (0.122.0 hat den 48-Fix noch nicht).
- 04:39: Batch 1 (bis 4ceb48e = 0.122.0) nach `stefanrows/OfflineDAoC` `main` gepusht. Batch 2 (0.125.0: 48, 59, 47C, 46) Build grün, Server-Tests 2447/1 übersprungen, Launcher 127/127; Deploy 04:40 (Backup `deploy-20260928-044025`), Start 04:41.
- Live-Messung 0.125.0 (04:41–04:57, 16 min, 1.210 Bots): 0 Exceptions, 0 Statusdatei-Fehler. RVR_KEEP_ROUTE_FAILED **0** (0.122.0: 49 in 17 min; 0.115.0: 519 in 3,4 h). Frontier-Teleports: 29 von 39 Abfahrten mit Gruppe ≥3 (party=3: 4, 4: 7, 6: 10, 7: 5, 8: 3; vorher 2 von 320). Tode 251 in 16 min (0.122.0: 698 in 17 min), davon PvP 84 (vorher 495); Tode in den drei Hubs 6 von 251 (vorher 82 %). Bug 29: 3 Fern-Leichen sofort freigegeben (REMOTE_CORPSE_RELEASE), keine Warteschleife. Ticks: Median 40 ms, p95 154, Max 622, NpcService Ø 5,2 ms. 47C: 12 Gruppen-Tasks gestartet, noch kein Deadline-Start (erwartungsgemäß erst nach 30 min). 47A: 12 Erholungen, 3 Camp-Wechsel.
- 05:00: Rest (0.123.0–0.125.0 + Bericht) gepusht; Server bleibt auf 0.125.0 laufen.

## Zusammenfassung für Aaron

**TL;DR**
- **9 Pakete gepusht (0.118.0 → 0.125.0), Server läuft lokal auf 0.125.0, Ked ist Co-Leader.**
- **Bot-Ticks live ~3× schneller, /tc geht, Leveln und RvR haben ihre bewiesenen Ursachen behoben.**
- **Offen für dich: Real-Client-Checks (Tabelle unten) und 3 Entscheidungen.**

### Was jetzt anders ist

| | Version | Was du merkst |
|---|---|---|
| ✅ | 0.118.0 | `/tc` teleportiert zuverlässig zur Realm Exchange; kein Startfehler mehr (Bug 57) |
| ✅ | 0.119.0 | Bot-PvE-Gruppen warten nicht mehr stundenlang auf eine Leiche zwei Zonen weiter; wer dreimal auf dem Rückweg stirbt, fliegt raus (Bug 29) |
| ✅ | 0.120.0 | Weniger Server-Ruckler: festgefahrene Bots suchen ihren Ausweichweg in Häppchen; Minuten-Profil `BOT_THINK_PROFILE` im Log (Bug 56) |
| ✅ | 0.121.0 | Solo-Weltbots trauen sich nach Toden wieder an Blau/Gelb, leveln bis 35 in der Nähe, und jeder Bot-Tod steht als Logzeile mit Killer (Task 47 A) |
| ✅ | 0.122.0 | Dashboard- und Weltgeschwindigkeits-Statusdatei fallen nicht mehr aus (Bugs 58, 26) |
| ✅ | 0.123.0 | Castle Sauvage, Svasud Faste, Druim Ligen sind sichere Zonen (3.500 Einheiten, auch für dich); Bots kommen aus den Portal-Keeps raus; Warbands geben unerreichbare Keeps auf und porten gemeinsam (Task 48, Punkte 1–5) |
| ✅ | 0.123.1 | Launcher-Testsuite grün (Bug 59) |
| ✅ | 0.124.0 | PvE-Gruppen kurz vor dem Camp starten ihren Task statt sich aufzulösen; Camps nahe am Treffpunkt (Task 47 C) |
| ✅ | 0.125.0 | `/petpull` ist ein Gruppenmodus: einmal an, dann normal mit dem Pet pullen (Task 46) |

### Ked
✅ GuildRank 9 → 1 in North Bomb, Backup `playable-backups/opendaoc.pre-ked-rank-20260928-041813.db`. Bitte im Spiel prüfen, ob Rang 1 einladen darf (Rang-Rechte der Gilde).

### Live gemessen (0.122.0 17 min, 0.125.0 16 min, je 1.210 Bots)

| Messwert | vorher (0.115.0) | jetzt (0.125.0) |
|---|---|---|
| lange Bot-Ticks pro Minute | ~85 | 20–36 |
| Tick-Median / Max | 94–122 ms / 3.967 ms | 40 ms / 622 ms |
| NpcService Ø / p95 | 12–15 ms / ~60 ms | 5,2 ms / 18 ms |
| Exceptions im Lauf | 2 | 0 |
| gescheiterte Keep-Routen | 519 in 3,4 h | 0 in 16 min |
| Frontier-Teleports als Gruppe (≥3) | 2 von 320 | 29 von 39 |
| Bot-Tode pro Minute (PvP) | ~75 (PvP ~29/min) | ~16 (PvP ~5/min) |
| Anteil der Tode an den drei Hubs | 82 % | 2 % |
| Warteschleifen auf Fern-Leichen | 10 Gruppen, bis 1 h 43 min | 0 (3 Sofort-Freigaben) |

### Bitte im Spiel prüfen

| | Was | Wie |
|---|---|---|
| ❓ | `/petpull` an, Pet schicken, Gruppe wartet, Tank taunted ein Add vom Pet | Enchanter/SM-Gruppe am Camp |
| ❓ | Sichere Hubs: du bist an Castle Sauvage / Svasud / Druim Ligen unangreifbar (3.500 Einheiten) | hinstellen, angreifen lassen |
| ❓ | Teleporter-Landepunkt Castle Sauvage liegt ~9.000 Einheiten vom Hub-Zentrum, Svasud ~4.000 → außerhalb der Schutzzone | hinporten, schauen, ob das der Burghof ist |
| ❓ | RvR-Gruppen im Feld (Emain, Odin's, Hadrian's) nach 1–2 h Laufzeit | `/who`, Launcher Active Groups |
| ❓ | Bot-Level steigen schneller (vorher ~0,5 Level/Bot-Stunde) | Launcher Active Population nach ein paar Stunden |
| ❓ | Ked kann einladen | `/gc invite` |

### Entscheidungen für dich

| | Frage | Optionen |
|---|---|---|
| ❓ | Spielstand auf die SSD? Die DB liegt auf der 5.400-rpm-HDD D:, langsame SQL-Anweisungen brauchen im Median 253 ms; lange Reaper-Ticks bei deinen Kills in DF (Ø 425 ms) hängen daran | a) DB auf SSD umziehen b) so lassen c) erst Reaper-Ursache mit dem neuen Profil bestätigen |
| ❓ | Schutzradius der Hubs vergrößern, damit der Teleporter-Landepunkt drin liegt? | a) Radius auf ~10.000 b) zweiter Kreis am Landepunkt c) so lassen |
| ❓ | Task 47 Paket B (Kollateral-PvP in BotBrain): die Live-Zahlen zeigen 406 gezielte vs. 89 kollaterale PvP-Tode in 17 min → eher Hub-Gemetzel (durch 0.123.0 adressiert) als Flächenzauber | a) B streichen und nach 24 h neu messen b) B trotzdem bauen |

### Nicht gemacht / offen
- Task 45 (Lastcheck Battlegroups) braucht zwei echte Spieler mit je 5 Squads → nur die Bot-Last wurde gemessen (siehe oben).
- Task 47 Paket B (Kollateral-PvP), Task 48 Punkt 6 (1.65-Feinschliff: Milegates, Rasten, Stealther-Paare, Doktrin) → erst wenn Gruppen draußen sind.
- Bug 20 (Camp-Reise-Timeouts) bleibt pending, 85 % erreichen ihr Camp; 47 C sollte den Rest senken.
- Neue Bugs 60–63 nur erfasst (Spell-Daten-Fehler beim Start, Epic-Raid-Hub-Route Albion, NULL-Styles, Bindstein = RvR-Sammelpunkt).
- Bug 56 Rest: NavPathQuery in TravelAcrossRegions/ZoneItineraryStep (bis 220 ms pro Runde), Koordinator-Lock; Reaper-Ticks bei Spieler-Kills noch nicht live gemessen (kein Spieler online).
- Werkzeug: `playable-dev/dbquery.py` scheitert bei laufendem Server am WAL („disk I/O error") und überschreibt seinen Snapshot; `server.sh` stellt das Launcher-Fenster jetzt vor jedem Klick wieder her (war minimiert → STOP-Knopf unauffindbar).
- Automatisierte Tests liefen (Server 2447, Launcher 127, alle grün); Real-Client-Prüfung ist deine.

## Live-Nachmessung 15:46 (0.125.0, 11 h Laufzeit, 1.210 Bots, kein Spieler online)

**TL;DR**
- **Leveln zieht an:** 165 Bots zwischen Level 30 und 41 (um 03:19 waren es 3, keiner über 32).
- **RvR findet im Feld statt:** 229 RvR-Bots lebend im Frontier (vorher 50), 5 statt 76 im Portal-Keep gefangen, 92 % der PvP-Tode sind gezielte 50er-gegen-50er-Kämpfe.
- **Aber:** PvE-Tode ~3× häufiger (Mobs an den Hubs und benannte Frontier-Mobs), Ticks schwanken stundenweise, 0 Keep-Eroberungen.

| | Messwert | vorher (0.115.0) | jetzt (0.125.0, 11 h) |
|---|---|---|---|
| ✅ | Exceptions | 2 pro Lauf | 0 |
| ✅ | Bots Level 30–49 / max. Level unter 50 | 3 / 32 | 165 / 41 |
| ✅ | RvR-Bots lebend im Frontier-Feld | 50 | 229 |
| ✅ | im gegnerischen Portal-Keep gefangen | 76 | 5 |
| ✅ | RvR-Datensätze ohne Ablaufzeit | 183 | 0 |
| ✅ | gescheiterte Keep-Routen | 519 / 3,4 h | 66 / 11 h, 20× sauber aufgegeben |
| ✅ | Frontier-Teleports als Gruppe (≥3) | 2 von 320 | 5.191 von 7.560 (1.999× volle 8er) |
| ✅ | 8er-RvR-Gruppen gebildet | 4 / 3,4 h | 13 / 11 h (+ 130× 2er, 98× 3er, 50× 4er) |
| ✅ | PvP-Tode: gezielt vs. kollateral | 65 % kollateral | 92 % gezielt (25.623 vs. 2.180); 78 % sind 45+ gegen 45+ |
| ✅ | PvP-Tode pro Stunde | 4.300–5.400 | ~2.500 |
| ✅ | Anteil Tode an den drei Hubs | 82 % | 10 % (alle PvE, siehe unten) |
| ✅ | Bug 29: längste Rez-Warteschleife pro Gruppe | 2.700 Zeilen, 1 h 43 min | 20 Zeilen; 905 Fern-Leichen sofort freigegeben, 49 Drops |
| ✅ | 47 C: Gruppen starten Task an der Reise-Deadline | – | 18× |
| ⚠️ | PvE-Tode pro Stunde | ~770 | 1.250 (05 h) → 3.660 (14 h), steigend |
| ⚠️ | Con-Grenze GRÜN bei Solo-Toden | 70 % | 22 % (05–06 h) → 39 % (14–15 h), rutscht wieder ab |
| ⚠️ | lange BotBrain-Ticks pro Stunde | 5.000–5.750 | 1.400–2.500, aber Spitzen 5.000 (06 h, 10 h); Median 42 ms, p99 1.189 ms, 455 über 1 s |
| ⚠️ | Camp-Reise-Timeouts (Bug 20) | ~7/h | ~7,6/h, unverändert |
| 🔴 | Keep-Eroberungen | 0 | 0; 3 Belagerungen gestartet, 33× „Siege defended: four-hour timer" |

**Neue Befunde**
- **Mobs an den Hubs:** 5.492 Tode innerhalb der Schutzzonen, alle PvE (phantom magi 735, savage dragonfly 519, thrawn ogre thresher 411, snowshoe bandit 307 …). Die Zonen schützen vor Spielern, nicht vor den restaurierten Frontier-Spawns um die Grenzburgen. Steigt über den Tag (206/h → 770/h).
- **RvR-50er sterben an benannten Frontier-Mobs:** 7.614 PvE-Tode von 50ern, Top-Killer „Illusion of Aidon the Archwizard" (Lvl 75, 589×), „Black Lady" (65, 457×), „reanimated guardian" (58, 337×). Ein 2003er 8er lief um solche Mobs herum.
- **Leveller-PvE-Tode:** 17.768 der 27.277 PvE-Tode sind Level 20–34 (SoloPve 14.547). Die Con-Erholung schickt Bots zu Blau/Gelb (2.604 Camp-Wechsel), wo sie häufiger sterben; die Grenze rutscht im Tagesverlauf wieder Richtung Grün. Netto leveln sie trotzdem deutlich schneller (siehe Tabelle) – das Verhältnis Kills/Tode braucht 24 h Daten.
- **Porter-Karussell:** 7.560 Frontier-Teleports in 11 h (vorher 94/h, jetzt ~690/h); einzelne Warbands porten 160–191× (alle 3–4 min): sterben im Feld → Release am Hub → sofort wieder porten. Menschlicher wäre eine Pause am Hub (rezzen, buffen, regruppieren, 2–5 min).
- **Tick-Spitzen:** neue Hotspots `ExecuteRvr` (einzelne Runden 260–480 ms, 42 s Summe in den Spitzenstunden) und `StableNetworkCache` (30-min-Neubau ~1 s, wie vom Reviewer erwartet). Bug 56 ergänzt; neue Bugs 65 und 66 erfasst.
- **PvP-Tode am Teleporter-Landepunkt Castle Sauvage:** 116 (außerhalb der Schutzzone), Zone Forest Sauvage insgesamt 7.163 PvP-Tode → die Radius-Entscheidung bleibt offen.

**Vorschläge (nichts davon gebaut)**
1. Frontier-Spawns im Hub-Radius entfernen oder Bots am Hub Mobs meiden lassen (Ursache der 10 %).
2. RvR-Bots meiden benannte Mobs ≥ Lvl 55 und Mob-Camps auf der Route (Aggro-Radius umgehen).
3. Nach Release am Hub 2–5 min regruppieren, bevor die Warband erneut portet; ggf. Teleport-Frequenz pro Warband deckeln.
4. Belagerung real machen (Task 48 Punkt 6): Rammen/Türen/Lord tatsächlich angreifen, sonst bleibt es bei 0 Eroberungen.
5. Bug 56 Runde 2: `ExecuteRvr` und `StableNetworkCache` in Scheiben; Koordinator-Lock.

## Abendrunde 2026-09-28 (18:00–20:40): Vorschläge 1–5, SSD-Umzug, Balance

**TL;DR**
- **Umzug auf die SSD:** alles liegt jetzt unter `C:\OfflineDAoC` (Repo, Install, Dev-Tools, Backups); `D:\OfflineDAoC` ist eine alte Kopie und kann gelöscht werden.
- **Fünf Pakete gebaut, reviewt, gemergt:** 0.134.0 Hubs/Charm-Pets, 0.135.0 Belagerung Slice 1, 0.136.0 Roaming, 0.137.0 Perf Runde 2, 0.138.0 Balance.
- **Live bis 0.136.0 bestätigt:** Hub-Tode 0, Teleports 690/h → ~96/h, erste Rammbock-Käufe seit dem 20.09.

| | Version | Was du merkst |
|---|---|---|
| ✅ | 0.134.0 | Charm-Pets von Sorcerer/Minstrel/Mentalist-Bots respawnen nicht mehr als wilde Mobs am Bindstein (war 47 % aller PvE-Kills); zweiter Schutzkreis an den äußeren Bindsteinen von Castle Sauvage und Svasud (Entscheidung B/b) |
| ✅ | 0.135.0 | Bot-Warbands belagern wieder: Rammbock kaufen, setzen, feuern; Nahkämpfer schlagen ans Tor, Caster reiten mit; Lord erst nach allen Toren; nur claimbare Keeps; leere Belagerung endet nach 45 min; nur eine volle Gilden-8er eröffnet |
| ✅ | 0.136.0 | RvR-Bots meiden Namensmobs ≥55 und dichte Camps, kein Dungeon-Tunnel mehr (Alb/Mid porten über Zuhause), regruppieren vor dem Porten (75 s nach Release, eine Abfahrt pro Warband je 5 min) |
| ✅ | 0.137.0 | ExecuteRvr-Spitzen waren SQLite-Lesezugriffe (Händlerlisten) im Denkschritt hinter langsamen HDD-Schreibzugriffen; jetzt Hintergrund-Refresh, Stallnetz-Neubau außerhalb der Runde, Stallsuche in Scheiben |
| ✅ | 0.138.0 | Wardens-Keeps auf Level 1 (Wachen 52, Lord 63, Tor 10.000 HP), eine Belagerung pro Gilde (max. 6), 3 Keeps pro Gilde, Direktschaden-Zauber und Bolts auf Tore mit 50 % (wie 1.46) |

**Live (0.136.0, 19:35–19:50, 1.184 Bots):** 0 Exceptions; Hub-PvE-Tode 0 (vorher 200–770/h); Geister-Mob-Kills 1 (vorher ~2.100/h); Teleports 24 in 15 min, keiner <75 s nach Release; 12 Rammbock-Käufe, 1 Belagerung gestartet, noch kein Tor-Treffer; 6 Mob-Umgehungen, 3 Abbrüche; NpcService Ø 4,5 ms.

**Gelernt**
- Die Hub-Tode kamen nicht von Frontier-Spawns (Advisor-Vermutung), sondern von leckenden Charm-Pets; die Landepunkte aus der Ausweichtabelle liegen außerhalb, die Teleport-Tabelle landet Spieler im Hub.
- Bots hatten seit dem 20.09. gar keinen Belagerungscode aktiv (`TryRunSiegeJob` ohne Aufrufer, Camlann Tier 4).
- Der SSD-Umzug allein senkte die ExecuteRvr-Spitze von 1.234 auf 255 ms.
- BAF-Patchgeschichte 1.15–1.125 geprüft: Pet-Pull-BAF wurde nie geändert; Stefans 0.133.0-Fix (BAF aus beim gehaltenen Pet-Pull) ist eine Erleichterung gegenüber jeder Live-Version.
- Deploy scheitert bei offenem Spiel-Client (`game.dll`); `server.sh update` prüft das jetzt vor dem Stopp.

**Bitte im Spiel prüfen**

| | Was | Wie |
|---|---|---|
| ❓ | Belagerung: Rammbock am Tor, Caster sitzen auf, Nahkämpfer stehen am Tor (nicht daneben) | einer Warband hinterher |
| ❓ | Level-1-Keeps: Mauerhöhe niedriger, schweben Wachen? | ein ehemals Level-5-Keep ansehen |
| ❓ | Nukes/Bolts treffen Tore mit halbem Schaden, DoT/CC nicht | selbst am Tor casten |
| ❓ | Kein Geister-Mob mehr am Bindstein von Castle Sauvage | dort stehen |
| ❓ | Petpull-BAF: willst du Stefans „BAF aus“ behalten oder 1.65-Adds auf die Gruppe? | Entscheidung mit Stefan |

**Offen**
- Tor-Treffer/`LordDefeated` live noch nicht gesehen (Truppen brauchen Marschzeit).
- 308 snowshoe-bandit-Tode am Svasud-Bindstein unerklärt (BUGS 65).
- Bug 56 Rest: SelectCamp/ZoneItineraryStep bis 1,1 s, erster synchroner Händler-/Netz-Load nach Start.
- Relikt-Raids als eigener Modus (TASKS 56), Belagerung Slice 2 (Anmarschrouten, 89 Exterior-Route-Fehler).
- NearestPorter ohne Realm-Filter; Rider im Spieler-Gruppen-Fall bleibt sitzen (Randfälle aus den Reviews).

**Abschluss 20:50:** 0.139.0 (= Balance auf Stefans 0.138.0 Petpull-Fix) gebaut, getestet (Server 2.632, Launcher 127 grün), gepusht und um 20:47 installiert. Start ohne Exception; `FRONTIER_BALANCE_PROPERTY` setzte starting_keep_level 4→1 und guilds_claim_limit 1→3; 18 Wardens-Keeps auf Level 1 (`FRONTIER_WARDEN_KEEP_LEVEL`). Server läuft. Beobachtung der ersten Belagerung mit 10.000-HP-Toren steht aus.

