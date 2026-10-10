# Art sources

This folder holds the files art is *made* in: `.blend` files, the palette's
paint source, reference images, and bought packs exactly as downloaded
(`<Name>/`). Godot never imports it (`.gdignore`) and Git LFS stores it. What
the game loads lives next door: `art/models/` (`.glb`), `art/voxels/` (`.vox`,
which is source and export at once), `art/textures/` and `art/materials/`.

**How a model gets into the game** (Blender or MagicaVoxel, made
destructible, animated by code or by hand) **is `docs/art-to-game.md`.** It
replaces the checklist that used to be here.
