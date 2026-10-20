"""Writes scenes/props/prp_vine_forest.tscn: baked vine scenes placed at random.

A test stand of vines for lvl_horde_flats, written once and then a scene like
any other: every vine is a node that can be moved by hand. Rerunning with the
same seed writes the same forest.

The vines go on the open ground round the level's built-up middle, at least
SPACING apart, as many as fit (up to COUNT).

    python tools/vine-forest/make_forest.py [count] [seed] [spacing]
"""
import math
import random
import sys
from pathlib import Path

COUNT = int(sys.argv[1]) if len(sys.argv) > 1 else 200
SEED = int(sys.argv[2]) if len(sys.argv) > 2 else 20261020
SPACING = float(sys.argv[3]) if len(sys.argv) > 3 else 100.0

# lvl_horde_flats: the ground is 1200 m square at the origin; keep 30 m off
# its edge. The compound, downtown, the trees and the first four vines stand
# in x -250..105, z -210..345: keep 60 m off that.
GROUND = 570.0
TAKEN = (-310.0, 165.0, -270.0, 405.0)  # x min, x max, z min, z max

VINES = [
    ("uid://7fca24rlt4ck", "res://scenes/props/prp_vine_41051.tscn"),
    ("uid://fd5827ugs3bj", "res://scenes/props/prp_vine_44300.tscn"),
    ("uid://b7k2n51522xq1", "res://scenes/props/prp_vine_51522.tscn"),
    ("uid://c4p8w31428q1a", "res://scenes/props/prp_vine_3142857.tscn"),
]


def free(x, z):
    return not (TAKEN[0] <= x <= TAKEN[1] and TAKEN[2] <= z <= TAKEN[3])


rng = random.Random(SEED)
spots = []
tries = 0
while len(spots) < COUNT and tries < 200000:
    tries += 1
    x, z = rng.uniform(-GROUND, GROUND), rng.uniform(-GROUND, GROUND)
    if free(x, z) and all((x - sx) ** 2 + (z - sz) ** 2 >= SPACING ** 2 for sx, sz, _, _ in spots):
        spots.append((x, z, rng.random() * math.tau, rng.randrange(len(VINES))))

lines = ["[gd_scene format=3]", ""]
for i, (uid, path) in enumerate(VINES):
    lines.append(f'[ext_resource type="PackedScene" uid="{uid}" path="{path}" id="{i + 1}_vine"]')
lines += ["", '[node name="prp_vine_forest" type="Node3D"]', ""]
for n, (x, z, yaw, kind) in enumerate(spots):
    c, s = math.cos(yaw), math.sin(yaw)
    lines.append(f'[node name="Vine{n:03d}" parent="." instance=ExtResource("{kind + 1}_vine")]')
    lines.append(f"transform = Transform3D({c:.5f}, 0, {s:.5f}, 0, 1, 0, {-s:.5f}, 0, {c:.5f}, {x:.2f}, 0, {z:.2f})")
    lines.append("")

out = Path(__file__).resolve().parents[2] / "scenes/props/prp_vine_forest.tscn"
out.write_text("\n".join(lines), encoding="utf-8", newline="\n")
print(f"{len(spots)} vines, {SPACING:.0f} m apart (seed {SEED}, {tries} tries) -> {out}")
