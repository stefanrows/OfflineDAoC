"""Stage the raid click-to-target fix for the installed native raid windows.

The raid windows (Custom9 for 40, Custom10 for 80 members) set each member
row's OnClickEvent to a name such as ``RaidMember00``. The stock XML parser
maps OnClickEvent through 0x4EA06F, which returns -1 (no event) for names it
does not know, so the rows never fired. The raid's event handler already
selects member ``i`` for event ID ``0x600 + i``; this fix writes that decimal
ID (``1536`` to ``1615``), which the parser accepts through atoi.

Only the four raid XML files change. game.dll is read to check that it is a
supported raid build and is never modified. The input client is read only.
"""

import argparse
import hashlib
import json
from pathlib import Path
import re


RAID_EVENT_BASE = 0x600
# game.dll builds that contain the verified raid event handler at 0x4E04DE:
# the native raid client, and that client with the Companion Manager on top.
SUPPORTED_GAME_SHA256 = {
    "67dcf68a37b95a93946a943b99d5e19b4a03e08cd6469275e25c7b909de21e99": "native raid",
    "3b6274dc385b90bf892f27d96c9e56cb457e1e94cbf45b5892d462a9e4d70890": "raid + Companion Manager 0.32.1",
    "88530c0093b285fd38fd6759e464373baa65ebb20473a949bc3b79fccbb41fd3": "raid + Companion Manager 0.33.0",
}
# Installed raid XML before the fix, as built by build_native_raid80_client.window().
WINDOWS = {
    "custom9_window.xml": (40, "6ff8e226ce4160e5fd4be87d1b91f45f59ae0df1a9c0cfdd122ec643d4e1a1d1"),
    "custom10_window.xml": (80, "aca82bf6c672a9fd0baec129cb457218d8ea700336d0db40ae752911ec7fc6f3"),
}
SKINS = ("atlantis", "isles")
NAMED_EVENT = re.compile(rb"<OnClickEvent>RaidMember(\d{2})</OnClickEvent>")


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def fix_window(data, capacity):
    """Replaces every RaidMemberNN event with its decimal ID; nothing else changes."""
    members = [int(match.group(1)) for match in NAMED_EVENT.finditer(data)]
    if sorted(members) != list(range(capacity)):
        raise ValueError(f"Expected RaidMember00-{capacity - 1:02d} exactly once each")
    fixed = NAMED_EVENT.sub(
        lambda match: b"<OnClickEvent>%d</OnClickEvent>" % (RAID_EVENT_BASE + int(match.group(1))), data)
    if b"RaidMember" in fixed:
        raise ValueError("Unexpected RaidMember text outside OnClickEvent")
    return fixed


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--client", type=Path, required=True, help="Installed client app directory (read only)")
    parser.add_argument("--output", type=Path, required=True, help="New staging directory")
    args = parser.parse_args()
    if args.output.exists():
        parser.error("Output directory already exists")
    game = sha256((args.client / "game.dll").read_bytes())
    if game not in SUPPORTED_GAME_SHA256:
        parser.error(f"Unsupported game.dll {game}: expected a verified native raid build")
    report = {"raidEventBase": RAID_EVENT_BASE, "installedGameSha256": game,
              "supportedGameSha256": sorted(SUPPORTED_GAME_SHA256), "windows": {}}
    staged = {}
    for name, (capacity, expected) in WINDOWS.items():
        outputs = set()
        for skin in SKINS:
            original = (args.client / "ui" / skin / name).read_bytes()
            if sha256(original) != expected:
                parser.error(f"ui/{skin}/{name} is not the verified raid window ({sha256(original)})")
            try:
                fixed = fix_window(original, capacity)
            except ValueError as error:
                parser.error(f"ui/{skin}/{name}: {error}")
            staged[(skin, name)] = fixed
            outputs.add(sha256(fixed))
        report["windows"][name] = {"capacity": capacity, "baselineSha256": expected,
                                   "outputSha256": outputs.pop()}
    args.output.mkdir(parents=True)
    for (skin, name), data in staged.items():
        (args.output / skin).mkdir(exist_ok=True)
        (args.output / skin / name).write_bytes(data)
    report["status"] = "staged raid click-to-target fix; offline emulation only until the owner's real-client check"
    (args.output / "manifest.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({name: value["outputSha256"] for name, value in report["windows"].items()}, indent=2))
    print("Staged only. No client or server was started or modified.")


if __name__ == "__main__":
    main()
