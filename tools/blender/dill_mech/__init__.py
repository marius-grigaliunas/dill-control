"""Dill Control's mech tools for Blender (docs/decisions/0046).

Import a mech's .vox from the project as rigid parts on bones, pose moves as
actions with IK on the feet, export each to a MechMove the game plays, and
mirror the pose live into the Godot editor's Lab tab. In the 3D view's
sidebar (N), tab 'Dill'.
"""
import bpy
from bpy.props import BoolProperty, StringProperty

from . import export, live, rig, vox


class DILL_OT_import_mech(bpy.types.Operator):
    """Import a mech's .vox (art/voxels/mch_*.vox) as parts on bones, or refresh its meshes"""
    bl_idname = 'dill.import_mech'
    bl_label = 'Import Mech .vox'
    bl_options = {'REGISTER', 'UNDO'}

    filepath: StringProperty(subtype='FILE_PATH')
    filter_glob: StringProperty(default='*.vox', options={'HIDDEN'})

    def execute(self, context):
        r = rig.import_mech(self.filepath)
        self.report({'INFO'}, f'{r.name}: {len(export.part_objects(r))} parts')
        return {'FINISHED'}

    def invoke(self, context, event):
        context.window_manager.fileselect_add(self)
        return {'RUNNING_MODAL'}


def _active_rig(context):
    o = context.active_object
    while o is not None and not o.get('dill_mech'):
        o = o.parent
    return o


class DILL_OT_export_move(bpy.types.Operator):
    """Bake the rig's action to resources/vehicles/moves/<mech>_<action>.tres"""
    bl_idname = 'dill.export_move'
    bl_label = 'Export Move'

    def execute(self, context):
        r = _active_rig(context)
        action = r.animation_data.action if r and r.animation_data else None
        if r is None or action is None:
            self.report({'ERROR'}, 'Select a mech rig with an action')
            return {'CANCELLED'}
        path, claimed = export.export_action(r, action)
        self.report({'INFO'}, f'{path}: {", ".join(claimed) or "nothing moves"}')
        return {'FINISHED'}


class DILL_PT_panel(bpy.types.Panel):
    bl_label = 'Dill Mech'
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = 'Dill'

    def draw(self, context):
        col = self.layout.column()
        col.operator(DILL_OT_import_mech.bl_idname)
        r = _active_rig(context)
        if r is not None:
            col.label(text=f"{r['dill_mech']}: {len(export.part_objects(r))} parts")
        col.operator(DILL_OT_export_move.bl_idname)
        col.prop(context.scene, 'dill_live', text='Live to Godot')


def _live_changed(self, context):
    if self.dill_live:
        live.start()


CLASSES = (DILL_OT_import_mech, DILL_OT_export_move, DILL_PT_panel)


def register():
    for c in CLASSES:
        bpy.utils.register_class(c)
    bpy.types.Scene.dill_live = BoolProperty(name='Live to Godot', default=False, update=_live_changed)


def unregister():
    del bpy.types.Scene.dill_live
    for c in reversed(CLASSES):
        bpy.utils.unregister_class(c)
