"""The pose, mirrored live into the Godot editor while it is posed: every
part's transform, in Godot's axes, sent to the editor bridge's lab_pose
command, which poses the mech in the Lab tab. Only while something changed.
"""
import json
import os
import urllib.request

import bpy

from .export import godot_transforms

INTERVAL = 0.1
_OPENER = urllib.request.build_opener(urllib.request.ProxyHandler({}))
_last = {}


def _send(project, args):
    with open(os.path.join(project, '.godot', 'editor_bridge.json'), encoding='utf-8') as f:
        info = json.load(f)
    req = urllib.request.Request(
        f"http://127.0.0.1:{info['port']}/cmd/lab_pose", data=json.dumps(args).encode('utf-8'), method='POST',
        headers={'X-Bridge-Token': info['token'], 'Content-Type': 'application/json'})
    with _OPENER.open(req, timeout=2) as r:
        return json.loads(r.read().decode('utf-8'))


def pose_args(rig):
    parts = {}
    for name, m in godot_transforms(rig).items():
        parts[name] = [round(m[r][c], 5) for c in range(4) for r in range(3)]
    return {'mech': rig['dill_mech'], 'parts': parts}


def _tick():
    scene = bpy.context.scene
    if not getattr(scene, 'dill_live', False):
        _last.clear()
        return None
    for rig in [o for o in scene.objects if o.get('dill_mech')]:
        args = pose_args(rig)
        key = json.dumps(args['parts'], sort_keys=True)
        if _last.get(rig.name) == key:
            continue
        try:
            _send(rig['dill_project'], args)
            _last[rig.name] = key
        except (OSError, ValueError, KeyError):
            pass  # the editor is away (reloading its code); try again next tick
    return INTERVAL


def start():
    if not bpy.app.timers.is_registered(_tick):
        bpy.app.timers.register(_tick, first_interval=INTERVAL)
