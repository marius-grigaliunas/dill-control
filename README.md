# Dill Control

A first-person horde shooter. A contract exterminator flies down to a planet
infested with twenty-five thousand pickles, swings over them on a grappling
line, wrecks their compound, brings down the giant that rules them, and sends
the mining company the invoice.

**The ground belongs to the horde. The air belongs to you.**

- [`docs/pitch.md`](docs/pitch.md): the game.
- [`docs/plan.md`](docs/plan.md): from here to a vertical slice, milestones M0 to M7.
- [`CLAUDE.md`](CLAUDE.md): the rules of the codebase.
- [`docs/decisions/`](docs/decisions/): why the rules are what they are.
- [`tools/project-board/index.html`](tools/project-board/index.html): the board.

## Requirements

- Godot 4.7.2-stable **mono** (C#)
- .NET SDK 8 or later
- Git LFS (`git lfs install` once per machine)
- Blender, for art

## Build and run

```
git lfs pull
dotnet build
<godot> --headless --path . --editor --quit    # first import
<godot> --path .
```

## Measure

```
<godot> --path . --script scripts/debug/bench.gd
```

Target: 60 fps at 1920x1080 native on an 11th-gen i5 with Iris Xe. `U` in any
level records every frame to `perflogs/`.
