"""A contact sheet of an action: the mech at chosen frames, in one picture.

    blender --background <file.blend> --python tools/blender/look_move.py -- <action> <view> <out.png> <frame> <frame> ...

view: `top` (from above, the mech facing up the picture, its left on the
left) or `side` (from its right, facing right). Workbench render, the parts
in their palette colours; nothing in the file is saved. Frames run along the
rows, three to a row.
"""
import math
import sys

import bpy
import numpy as np

action_name, view, out = sys.argv[sys.argv.index('--') + 1:][:3]
frames = [int(f) for f in sys.argv[sys.argv.index('--') + 4:]]

scene = bpy.context.scene
rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
rig.animation_data.action = bpy.data.actions[action_name]

scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'
scene.display.shading.color_type = 'VERTEX'
scene.display.shading.show_cavity = False
scene.render.resolution_x = scene.render.resolution_y = 420
scene.render.film_transparent = False
world = bpy.data.worlds.new('look') if scene.world is None else scene.world
scene.world = world
world.color = (0.55, 0.62, 0.7)

ground = bpy.data.objects.new('look_ground', bpy.data.meshes.new('look_ground'))
ground.data.from_pydata([(-14, -14, -0.02), (14, -14, -0.02), (14, 14, -0.02), (-14, 14, -0.02)], [], [(0, 1, 2, 3)])
scene.collection.objects.link(ground)

cam = bpy.data.objects.new('look_cam', bpy.data.cameras.new('look_cam'))
cam.data.type = 'ORTHO'
scene.collection.objects.link(cam)
scene.camera = cam
if view == 'top':
    cam.data.ortho_scale = 13.0
    cam.location = (0, -0.5, 30)
    cam.rotation_euler = (0, 0, math.radians(180))
else:
    cam.data.ortho_scale = 14.0
    cam.location = (-30, 0, 5.0)
    cam.rotation_euler = (math.radians(90), 0, math.radians(-90))
cam.data.clip_end = 200

tiles = []
for f in frames:
    scene.frame_set(f)
    scene.render.filepath = f'{out}.{f}.png'
    bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(scene.render.filepath)
    px = np.empty(img.size[0] * img.size[1] * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    tiles.append(px.reshape(img.size[1], img.size[0], 4))

cols = 3
rows = (len(tiles) + cols - 1) // cols
h, w = tiles[0].shape[:2]
sheet = np.ones((rows * h, cols * w, 4), dtype=np.float32)
for i, t in enumerate(tiles):
    r, c = divmod(i, cols)
    sheet[(rows - 1 - r) * h:(rows - r) * h, c * w:(c + 1) * w] = t
img = bpy.data.images.new('sheet', cols * w, rows * h)
img.pixels.foreach_set(sheet.ravel())
img.filepath_raw = out
img.file_format = 'PNG'
img.save()
print('[look]', out, frames)
