# Companion Stage 5 character and control decisions

Updated: 2026-09-24. The owner chose individual role and engagement preferences,
with personality providing defaults and direct player orders taking precedence.
Dialogue appears on roster events and when requested, without combat chatter.
The implementation was built offline. The owner marked real-client and
combat-role acceptance complete on 2026-09-24; detailed observations were not
supplied.

## Cast and identity

The catalog has two individually written people for each of the 39 Classic + SI
classes. Each has a stable authored key, name, eligible race, gender, visible
size, background, personality, greeting, and field note. A character may recruit
an authored individual once; the existing 78-slot roster limit still applies.
`/companions` browses the cast and biographies, and
`/companions recruit authored <name>` is the command fallback. Generated recruits
remain available by class and now receive a saved personality template. Their
stable ID, name, class, race, gender, and progression remain independent.
Existing generated companions retain their identity and gameplay defaults.

A companion's preferred build is the enabled validated class plan when one
exists. The six blocked classes remain manual-only; Stage 5 does not invent
unsupported specialization plans. Owner training and equipment choices keep
precedence over the catalog recommendation.

## Tactical controls

`/companions role <name> <role>` selects a class-legal tank, healer, buffer, or
attacker job. The class policy validates choices. A tank job affects ordered
pull selection; healer and buffer jobs use the support combat path. Preferences
are saved on the companion record and apply only to persistent player-owned
companions. Generated and authored recruits start with their class's existing
combat role. Existing records with no role field keep that role too.

`/companions stance <name> aggressive|defensive` saves an individual's
engagement preference. A steady or gentle personality defaults to defensive;
other personalities default to aggressive. Defensive companions hold within
350 units of their owner or defend against immediate threats. Direct `/pull`
and owner attack orders can target beyond that radius within the existing group
assist range. `/aggressive` and `/defensive` issue a temporary group order that
supersedes individual preferences; `/companions group default` clears that
override. Temporary `/spawn` helpers retain their old group-order behavior.
Autonomous world bots never read these preferences.

`/companions profile <name>` and the private menu show background, voice,
preferred build, role, and stance. Recruitment, invite, and bench each emit one
line of dialogue in the system window. No periodic or combat-triggered dialogue
is emitted.

## Verification boundaries

- Offline Release server build completed. The cast was statically checked for
  78 unique names, two people per supported class, and eligible class/race
  pairings. The launcher version pin was also updated.
- Automated tests were not requested or run. In particular, class-role skill
  use and save-upgrade behavior have not been exercised in a disposable runtime.
- In the real client, browse the cast, recruit both people of one class, check
  uniqueness and stable appearance after restart, change role and stance,
  issue direct and group orders, and confirm dialogue appears only at roster
  events. Check healer, buffer, tank, and attacker behavior with trained skills.
  The Stage 4 client checks remain open as recorded in its brief.
