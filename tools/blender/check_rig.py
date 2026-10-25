"""Checks the add-on in a background Blender, away from any open file:

    blender --background --factory-startup --python tools/blender/check_rig.py -- <mech.vox> [out.json]

Imports the mech, prints each part's rest transform in Godot's axes (to
compare with the game's import), and writes them as JSON.
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy  # noqa: E402
import dill_mech  # noqa: E402
from dill_mech import export, rig  # noqa: E402

args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
path = os.path.abspath(args[0])
out = args[1] if len(args) > 1 else None
dill_mech.register()
for o in list(bpy.data.objects):
    bpy.data.objects.remove(o)
r = rig.import_mech(path)
rest = export.godot_transforms(r)
rows = {}
for name, m in sorted(rest.items()):
    t = m.to_translation()
    rows[name] = [round(t.x, 4), round(t.y, 4), round(t.z, 4)]
    print(f'{name:10s} {t.x:8.4f} {t.y:8.4f} {t.z:8.4f}')
print('bones:', [(b.name, b.parent.name if b.parent else '-') for b in r.data.bones])
if out:
    json.dump(rows, open(out, 'w'))
