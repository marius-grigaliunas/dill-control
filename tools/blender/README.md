# Blender tools

`dill_mech/` is our Blender add-on for the mechs' moves
(`docs/decisions/0046`). Blender 5.1. It is not a Godot addon: Godot never
sees `tools/`.

## Install

Make the zip Blender installs, from the project folder:

    python -c "import shutil; shutil.make_archive('dill_mech', 'zip', 'tools/blender', 'dill_mech')"

then in Blender: Edit > Preferences > Get Extensions > the menu at the top
right > Install from Disk, and pick `dill_mech.zip`. Its panel is in the 3D
view's sidebar (N), tab **Dill**. Delete the zip afterwards; it is not kept.

## Use

1. **Import Mech .vox** and pick `art/voxels/mch_*.vox`. Every part comes in
   as its own object fixed whole to a bone of its own (rigid, never
   skinned), at exactly the place and size the game imports it (checked:
   `check_rig.py`). The bones chain as the game's damage map chains the
   parts; each foot has an IK handle (`IK.FootL` ...). Markers are empties.
   Importing again after the model changed swaps the meshes and keeps the
   bones and actions.
2. **Pose a move** as an action on the rig. Timeline markers on the action
   are its events: `hit` (when blows land), `sfx:<name>`, `shake`.
3. **Export Move** writes `resources/vehicles/moves/<mech>_<action>.tres`
   (a `MechMove`). A part that moves at all is in the move; the rest keep
   walking in the game. No rebuild: the asset lab's `lab_do move` reads it
   fresh, and `MechSlashStats.RightMove`/`LeftMove` play a stroke.
4. **Live to Godot** mirrors the pose into the Godot editor's Lab tab while
   you pose (the editor bridge's `lab_pose`), ten times a second while
   something changes.

Work in your own `.blend` under `art/_source/<Mech>/`.
`art/_source/FieldFighter/fighter_moves.blend` holds the fighter's slam
(`slam_r`, `slam_l`), made by `make_slam.py` on the curves the game used to
compute.

## Scripts (background Blender, no open file touched)

    blender --background --factory-startup --python tools/blender/check_rig.py -- art/voxels/mch_field_fighter.vox
    blender --background --factory-startup --python tools/blender/make_slam.py
    blender --background art/_source/FieldFighter/fighter_moves.blend --python tools/blender/check_live.py -- slam_r 17
