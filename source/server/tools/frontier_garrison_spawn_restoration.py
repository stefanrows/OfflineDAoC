#!/usr/bin/env python3
"""Read-only selection of archived Old Frontier camp populations.

Uses the retained pre-restoration world and earlier reviewed manifests as the
zone/species roster. Coordinates, templates, levels and loot come from the
archive; this is a roster-backed density restoration, not a claim that each
archived coordinate has an independent 1.65 map witness. No cap of two or three
mobs per camp is imposed. The database is never written.
"""
import argparse
import collections
import json
import sqlite3
from pathlib import Path

ZONES = {11, 12, 14, 15, 111, 112, 113, 115, 210, 211, 212, 214}
ROOT = Path(__file__).resolve().parents[2]
DATA = ROOT / 'server/GameServer/bots/autonomous/data'


def select(database):
    prior = set()
    for name in ('classic165_restored_spawn_ids.txt', 'classic165_period_restored_spawn_ids.txt'):
        prior.update((DATA / name).read_text().splitlines())
    with sqlite3.connect(f'{database.resolve().as_uri()}?mode=ro', uri=True) as connection:
        zones = [row for row in connection.execute(
            'SELECT ZoneID,RegionID,Name,OffsetX*8192,OffsetY*8192,Width*8192,Height*8192 FROM Zones') if row[0] in ZONES]
        query = "SELECT Mob_ID,Name,Region,X,Y,Z,Level FROM {} WHERE Realm=0 AND ClassType='DOL.GS.GameNPC' AND Level BETWEEN 1 AND 65"
        archived = list(connection.execute(query.format('offline_classic165_removed_mobs')))
        archived_ids = {str(row[0]) for row in archived}
        # Exclude newly restored rows from the witness set, so rerunning after
        # applying this manifest cannot expand its own authorization.
        retained = [row for row in connection.execute(query.format('Mob')) if str(row[0]) not in archived_ids]
        retained += [row for row in archived if str(row[0]) in prior]
    def zone_of(row):
        return next((zone for zone in zones if zone[1] == row[2] and
            zone[3] <= row[3] < zone[3]+zone[5] and zone[4] <= row[4] < zone[4]+zone[6]), None)
    def name(row): return ' '.join(row[1].casefold().split())
    roster = collections.defaultdict(list)
    for row in retained:
        zone = zone_of(row)
        if zone: roster[(zone[0], name(row))].append(row)
    selected = []
    for row in archived:
        if str(row[0]) in prior: continue
        zone = zone_of(row)
        if not zone: continue
        witnesses = roster[(zone[0], name(row))]
        if not witnesses or not any(abs(row[6]-w[6]) <= 3 for w in witnesses): continue
        # Do not double a retained spawn standing at the same coordinates.
        if any((row[3]-w[3])**2+(row[4]-w[4])**2 < 64**2 and abs(row[5]-w[5]) < 128 for w in witnesses): continue
        selected.append({'mob_id': str(row[0]), 'name': row[1], 'zone_id': zone[0],
            'region_id': row[2], 'world_x': row[3], 'world_y': row[4], 'z': row[5], 'level': row[6]})
    return sorted(selected, key=lambda row: row['mob_id'])


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--database', type=Path, required=True)
    parser.add_argument('--ids', type=Path, required=True)
    parser.add_argument('--report', type=Path, required=True)
    args = parser.parse_args()
    rows = select(args.database)
    args.ids.write_text(''.join(row['mob_id']+'\n' for row in rows), encoding='ascii')
    args.report.write_text(json.dumps({'spawns': rows}, indent=2)+'\n')
    print(json.dumps({'total': len(rows), 'by_zone': dict(sorted(collections.Counter(row['zone_id'] for row in rows).items()))}))
