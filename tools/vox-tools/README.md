# Vox tools

Small Python scripts for getting bought or old voxel art into the shape
`art/_source/README.md` asks for. Python 3, no packages. Run from the project
folder. Nothing here runs in the game; Godot never sees this folder.

| Script | What it does |
|---|---|
| `obj2vox.py <in.obj> <palette.png> <out.vox>` | Rebuilds a `.vox` from MagicaVoxel's own OBJ export, for a pack that shipped without its `.vox`. Exact on the Quadruped Tank (every voxel and 99% of colours). Materials do not survive an OBJ: set Emit again afterwards. |
| `split_tank.py <in.vox> <out.vox>` | How the Quadruped Tank was split into parts, pivots and markers. |
| `split_mech.py <fighter\|storage> <in.vox> <out.vox>` | The same for the Field Fighter and the Mobile-storage Robot, with the pivot found automatically: the centre of a part's top layer, one voxel above it. |

The split scripts are records of one-off jobs, kept so a re-download of the
pack can be split the same way. For a model of your own, split it by hand in
MagicaVoxel; it is quicker than writing a region table.
