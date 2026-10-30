# 0044 The editor bridge: Claude works in the running editor, on the real scene

*24 October 2026. Agreed with the user. Builds on 0004 and 0043.*

## Context

The user builds by looking: placing, dragging and sculpting in the Godot
editor. Claude built by editing `.tscn`, `.tres` and `.cs` files and benching
the game, and never saw the editor. The two met badly in `lvl_drop_test`:
its ground was `ArenaTerrain`, a tool script that turned nine
`TerrainFeature` markers and some noise into terrain when the scene loaded.
In the user's viewport the terrain never appeared, and even when it did,
the user was editing stand-ins for the world, not the world.

## Decision

**Everything in a scene is the real thing, visible in the editor as itself.
Claude works on it through a bridge into the running editor, making the same
nodes the user would make by hand, each undoable with Ctrl+Z.**

- **The plugin.** `addons/editor_bridge` (C#, ours). Godot's own `TCPServer`
  on `127.0.0.1` only, polled every editor frame, so every command runs on
  the main thread with no queue. HTTP POST with JSON, one command a request.
  It listens only while the plugin is on and the editor is open. It writes
  its port and a random token to `.godot/editor_bridge.json` (gitignored)
  and deletes the file when it stops. A request without the token, or with
  an `Origin` header (a web page), is refused. There is no command that runs
  arbitrary code. It closes its port when a rebuild unloads the old code, and
  the new code opens it again, as the Vines tool rebuilds its panel.
- **Tools register commands.** `Bridge.Register(name, description, schema,
  handler)`; the bridge knows no tool. The Vines tool's tabs get methods that
  their buttons, the bridge and `reload-check` all call; nothing is driven by
  finding a button by its label.
- **The MCP server.** `tools/editor-bridge/godot_mcp.py`: Python's standard
  library only (MCP is JSON-RPC over stdin and stdout), registered in the
  project's `.mcp.json`. It asks the editor for its commands and turns a PNG
  in a result into an image. The same file is a command line
  (`python godot_mcp.py call view`) for testing by hand.
- **Undo.** Every command that changes a scene is one `EditorUndoRedoManager`
  action, and `batch` makes many one.
- **Standing on things.** `place` stands an asset's origin on the highest
  mesh under it, found from the meshes themselves (`TriangleMesh`), not from
  physics, which the editor does not step: what you see is what it stands on.
- **Seeing.** `view` returns the user's 3D view; given a position or a node,
  Claude renders through a camera of its own into the same world, so the
  user's view never moves.
- **Open files.** While a scene is open in the editor, Claude changes it
  only through the bridge, never by editing its `.tscn`.

**Terrain is sculpted, not generated.** It is ordinary nodes with their data
on disk: a `StaticBody3D` with a `HeightMapShape3D` whose heights are the one
copy of the ground, and chunk `MeshInstance3D`s made from them and saved as
`.res`. Nothing builds them when the scene opens. Our own brushes in the 3D
viewport (raise, lower, smooth, flatten, pad) change the heights and redraw
the chunks under them, one stroke an undo; the user sculpts with them, and
Claude through `sculpt`. A mesa Claude makes is a stroke, after which there
are only heights. The horde reads the shape's heights. Blender stays a way to
start a ground, imported into the heights once.

The user's calls (24 October):

- The terrain is our own brush, which the user uses too.
- Claude may judge vines Good or Bad when it can judge them accurately. Its
  verdicts carry `"by": "claude"`, so the logs always tell them from the
  user's.
- Claude may run the game window (`run`, a nest's Play) without asking.

## What was not chosen

- **Sculpting in Blender** as the main way: blind to the props, vines,
  lighting and the player's scale, a round trip for every change, and the
  horde would still need heights sampled from the mesh.
- **A painted heightmap**: painted blind in 2D, and an import step turns it
  into ground.
- **The bridge in GDScript**, which would survive a C# rebuild, but could not
  call the game's own classes as directly.

## What it costs

- A third editor plugin to keep working across rebuilds; the bridge is down
  for the seconds a reload takes, and the MCP server waits.
- The ground is a height field only: arches and caves are props or voxel
  models.
- Benches, screenshots of the running game and 2D are not in the first
  version.
