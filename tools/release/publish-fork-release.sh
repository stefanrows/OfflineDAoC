#!/usr/bin/env bash
# Package the already-built Release outputs as a fork update pack and publish
# it as GitHub release v<version> on stefanrows/OfflineDAoC (never upstream).
# Run after a successful build and push; HEAD must already be on origin/main.
# Usage: publish-fork-release.sh [--dry-run] [build_fork_release.py options]
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
REPOSITORY="stefanrows/OfflineDAoC"
INSTALL_ROOT="${OFFLINE_DAOC_ROOT:-/mnt/d/Games/OfflineDAoC}"
DEV_ROOT="${OFFLINE_DAOC_DEV:-${INSTALL_ROOT}-dev}"

dry_run=0
if [[ "${1:-}" == "--dry-run" ]]; then
  dry_run=1
  shift
fi

version="$(sed -n 's/.*DisplayVersion = "\([0-9.]*\)".*/\1/p' "$REPO_ROOT/source/tools/OfflineDaoc.Launcher/MainForm.cs")"
tag="v$version"
head="$(git -C "$REPO_ROOT" rev-parse HEAD)"
out="$DEV_ROOT/releases/$tag-$(date +%Y%m%d-%H%M%S)"

if [[ $dry_run -eq 0 ]]; then
  git -C "$REPO_ROOT" fetch -q origin main
  if ! git -C "$REPO_ROOT" merge-base --is-ancestor "$head" origin/main; then
    echo "publish-fork-release.sh: HEAD $head is not on origin/main; push first." >&2
    exit 1
  fi
  if gh release view "$tag" --repo "$REPOSITORY" >/dev/null 2>&1; then
    echo "publish-fork-release.sh: release $tag already exists on $REPOSITORY." >&2
    exit 1
  fi
fi

python3 "$SCRIPT_DIR/build_fork_release.py" --install-root "$INSTALL_ROOT" --output "$out" "$@"

if [[ $dry_run -eq 1 ]]; then
  echo "Dry run: packaged $tag in $out; nothing was published."
  exit 0
fi

gh release create "$tag" --repo "$REPOSITORY" --target "$head" \
  --title "Offline DAoC fork $tag" --notes-file "$out/release-notes.md" \
  "$out/fork-release.json" \
  "$out/OfflineDAoC-Fork-$version-Update.zip" \
  "$out/UPDATE-OFFLINE-DAOC.cmd" \
  "$out/Update-OfflineDAoC.ps1" \
  "$out/SHA256SUMS.txt"
echo "Published $tag from $head; package kept in $out"
