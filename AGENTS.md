# Offline DAoC: start here

This project contains AI-developed customizations of existing DAoC server projects.
Read README.md, CHANGELOG.md, docs/DEVELOPMENT.md, and the relevant component's
AGENTS.md before editing. Camlann conversion (in progress, tier by tier):
docs/CAMLANN.md. Work only on the tier the owner asks for.

## Bug and task tracking

- Record bugs in `docs/BUGS.md`; record rapid-fire tasks, feature requests, and
  ideas in `docs/TASKS.md`. Read the relevant tracker before working on an item.
- Update tracked items as part of completing their work. Follow each tracker's
  verification-pending and Finished rules; never leave completed items in Open.
- Mark finished tasks Done with a brief result and completion version or date.
  Keep work awaiting installation or real-client checks in the pending section.
- Capturing an idea is not authorization to implement the rest of the backlog.

## Changelog and versioning

This fork uses MAJOR.MINOR.PATCH. The launcher pin `DisplayVersion` and the
latest dated heading in CHANGELOG.md must match. The upstream playable
download stays GitHub v0.3; do not rewrite Get-OfflineDAoC.ps1 or the PLAY.md
download steps when bumping this fork.

Every completed change set must:

- Add a dated version heading in CHANGELOG.md with Added / Changed / Fixed /
  Removed bullets. Keep the Unreleased section empty between tasks; do not
  pile work there and forget to bump.
- Bump once per finished task, using the highest applicable level:
  - PATCH (0.3.1 → 0.3.2): docs, agent rules, tests-only, comments, or
    tooling that does not change gameplay.
  - MINOR (0.3.1 → 0.4.0): new or changed gameplay or launcher behavior that
    still loads the existing save.
  - Pre-1.0.0 major-scope changes: save/schema incompatibility, required
    progress import, a native client patch, or a new playable package. Until
    the owner explicitly declares the stable fork release, keep using the
    current three-part 0.x versioning scheme; do not promote a task or tier to
    1.0.0 automatically. Use 1.0.0 only when the owner explicitly says to
    release 1.0.
- Keep these in lockstep with the changelog heading:
  - `source/tools/OfflineDaoc.Launcher/MainForm.cs` (`DisplayVersion`)
  - `source/tools/OfflineDaoc.Launcher.Tests/LauncherPresentationTests.cs`
  - `ALL SERVER COMMANDS.txt` header
- Never put live save data, credentials, or hashes of personal databases in
  the changelog.

Do not jump to a bare `0.4` label. Three-part versions keep this fork
distinct from the original author's private 0.4 launcher.

## Git and GitHub: fork only

This checkout is the fork `stefanrows/OfflineDAoC`. The upstream project
`shadowofze/OfflineDAoC` is read-only for agents.

- Target **only the fork** for every GitHub action: pushes, branches, PRs,
  issues, comments, releases. Never open, comment on, or push to anything in
  `shadowofze/OfflineDAoC`.
- Always pass `--repo stefanrows/OfflineDAoC` to `gh` commands that create or
  change something (`gh pr create`, `gh pr merge`, `gh issue create`, ...).
  `gh` otherwise defaults to the fork's parent. Check with
  `gh repo set-default --view` before the first write in a session.
## Fast local R&D shipping

This project workflow overrides the generic merge-to-main skill for this fork
until the owner replaces it. Leave the shared skill unchanged.

- "ship now" and "merge to main" authorize the entire workflow below without
  another confirmation: build, merge directly to `main`, push to
  `stefanrows/OfflineDAoC`, stop this installation's running components, and
  deploy to `D:\Games\OfflineDAoC`. Ordinary development requests do not ship.
- Skip PR creation, CI checks/waits, Docker checks/builds, automated test suites,
  and post-deploy monitoring. Run tests only when explicitly requested. The
  upstream Docker files are not part of this Windows local workflow; Docker
  availability is not a blocker. Gameplay verification belongs to the owner.
- Review the scoped diff, conflict markers, and version pins once. Preserve
  unrelated work; never force-push, discard changes, or bypass actual conflicts.
  Fetch the fork's `main` and integrate it before building so the artifacts
  represent the revision being shipped. Reuse the current task branch; do not
  create a branch or PR just to ship. A rejected push requires reconciliation
  and rebuilding if the source changes, not a force-push.
- Build both the server entry project and Windows launcher in Release on every
  shipping invocation, including docs-only tasks. Use incremental builds and
  cached dependencies; restore only when missing/stale assets require it.
  Use the commands in `docs/DEVELOPMENT.md`; keep build output outside the game
  installation. Successful builds are required before merging/pushing.
- Commit only the intended task changes with the required version/docs updates,
  merge locally into `main` (fast-forward when possible), and push to the fork.
  Do not add another version bump if this task already has its finished bump.
- After a successful push, stop only processes verified by executable path
  (or the hosted server's command line) as belonging to `D:\Games\OfflineDAoC`.
  Prefer graceful shutdown where available; stopping those components, including
  termination if needed, is pre-authorized. Never kill by process name alone.
  If identity cannot be verified or shutdown fails, report the blocker rather
  than terminating unrelated processes or deploying over running files.
- Deploy those same server and launcher outputs with `tools/dev/deploy.sh`, both
  `-ServerBuild` and `-LauncherBuild`, and `-Apply`. Keep the existing backups,
  hash verification, rollback, and protected save/configuration checks. Do not
  use `-IncludeThirdParty` without a separate request. Leave the game stopped.
- Report merge/push and local deployment separately, including the deployed
  version and backup path (or no changed files). Never call a failed deployment
  successful shipping. State that automated tests were skipped and real-client
  verification is left to the owner; do not wait for CI or gameplay acceptance.

## Safety defaults

- Resolve paths from this checkout, never from the original author's Windows username.
- Preserve each file's existing line endings. Do not convert whole files between LF and CRLF.
- Distinguish real players, companion bots, and autonomous gamebots before changing AI.
- Preserve saves, real inventories/loot/coins, equipment upgrades, realm exchange,
  and existing travel behavior unless the owner explicitly asks to change them.
- Historical scripts in source/server/tools may contain absolute deployment paths
  and destructive migration operations. Read and parameterize them before use.
  Do not execute them just because they are present. They are not the bootstrap.
- Never start a game server without permission or deploy over a running installation.
  Shipping authorization above covers stopping this installation and deploying
  after it has stopped; it does not authorize starting it again.
- Build/test in a separate output tree. Never publish a live SQLite database,
  account.txt, bot profiles, credentials, logs, dumps, or previous Git history.
- Keep native client patch hash guards. Never apply a binary patch to an unverified
  client build. Texture atlases are not mesh/skeleton replacements.
- Do not globally rebuild/replace navigation to fix one local route without evidence.
- Report offline/static checks separately from real-client gameplay verification.
- The user may customize their fork's rules. These are safety defaults, not a ban
  on intentional gameplay changes requested by the fork owner.
