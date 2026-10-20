"""Writes the drop test's jar: art/voxels/prp_jar_block.vox. Graybox, not art.

Run from the project folder (needs the voxel-art skill's voxkit on the path):
    python tools/vox-tools/build_prp_jar_block.py <voxkit dir>

At 0.5 m a voxel (the importer's voxel/size for this file) it stands 40 m
tall and 24 m across. Two objects, both static:

- Body: the jar. Walls 3 m thick at the foot, thinning to 1.5 m at the
  shoulder, and a 1 m lid. Blasting through the side is a long job; landing
  on the lid and cutting down is the short one, and getting up there is the
  line's job.
- Core: a glowing column inside, from the ground to 8 m under the lid. The
  mission counts the jar cracked when enough of it is gone.
"""
import os
import sys

sys.path.insert(0, sys.argv[1] if len(sys.argv) > 1 else ".")
from voxkit import Model, Palette, save_vox_scene  # noqa: E402

W, H = 48, 80             # voxels across, voxels tall
C = (W - 1) / 2.0         # centre
R = 23.5                  # outer radius
LID = 2                   # lid thickness
CORE_R, CORE_TOP = 5.5, H - LID - 16

pal = Palette()
pal.ramp("glass", "#9fc7b4", 3)
pal.ramp("lid", "#c23a2a", 3)
pal.add("inside", "#2c3a2a")
pal.add("core", "#b8ff3a", emit=(0.8, 2))
pal.add("core_dark", "#3f6a14")

body = Model(W, W, H, pal)
core = Model(W, W, H, pal)

for z in range(H):
    # Thickness in voxels: 6 at the foot to 3 at the shoulder.
    t = 6 - round(3 * z / (H - LID - 1))
    lid = z >= H - LID
    neck = R - 2 if z >= H - LID - 6 else R
    radius = neck if not lid else neck + 1
    for y in range(W):
        for x in range(W):
            d = ((x - C) ** 2 + (y - C) ** 2) ** 0.5
            if d > radius:
                continue
            if lid:
                body.set(x, y, z, "lid_1" if (x + y) % 6 else "lid_2")
            elif d > radius - t:
                band = z % 8 == 0
                body.set(x, y, z, "glass_2" if band else ("glass_1" if d > radius - 1 else "inside"))

core.cylinder(C, C, 0, CORE_TOP, CORE_R, "core_dark")
core.cylinder(C, C, 0, CORE_TOP, CORE_R - 1, "core")

out = os.path.join("art", "voxels", "prp_jar_block.vox")
save_vox_scene(out, [(body, (0, 0, 0), "Body"), (core, (0, 0, 0), "Core")])
print("wrote", out)
