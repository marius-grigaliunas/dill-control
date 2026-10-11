# Modular menu

A pause-menu stack with no dependency on the game around it. Four files, one
namespace (`ModularMenu`), no scenes, no autoloads, no project settings.

## Dropping it into another project

1. Copy this folder anywhere under the project's script root.
2. Add a node of type `MenuSystem` (it is `[GlobalClass]`, so it appears in the
   Create Node dialog) somewhere that is always in the tree.
3. Give it a `Root` screen, either a `.tres` authored in the inspector or one
   built in code.
4. Connect `SettingChanged`, `CustomActivated` and `QuitRequested`.

That is the whole integration. It needs no input actions of its own: it drives
off the engine's built-in `ui_up`, `ui_down`, `ui_left`, `ui_right`,
`ui_accept` and `ui_cancel`, which exist in a blank Godot project. While the
menu is open, the pad's east button also goes back, so that button can stay a
gameplay action and not open the menu. A rebind row takes a key, a mouse
button, a pad button or a stick axis; Start or Esc cancels, and a new binding
replaces only its own kind.

## The pieces

| File | What it is |
|---|---|
| `MenuAction.cs` | What a row does: Resume, OpenScreen, Back, Toggle, Slider, Custom, Quit |
| `MenuEntry.cs` | One row. A `Resource`, so menus are data |
| `MenuScreen.cs` | A titled list of entries. Submenus are entries pointing at other screens |
| `MenuSystem.cs` | The `CanvasLayer` that pauses, draws, navigates and raises signals |

## The boundary

`MenuSystem` stores and displays settings; it never applies them. A `Toggle` or
`Slider` row raises `SettingChanged(id, value)` and the host does the work. The
host seeds the current value once with `DefineSetting(id, value)` so the menu
opens showing what the game is actually doing.

This is what keeps the copy portable. The moment this class knows what a field
of view is, it stops being droppable and starts being one game's menu.

`Quit` is the same trade in miniature: with nothing connected to
`QuitRequested` it calls `GetTree().Quit()`, and with a listener it hands over
entirely so a host that needs to save first can.

## Worked example

`scripts/ui/GamePauseMenu.cs` in this project is the host half: it builds the
screens, seeds six settings, and applies them. It is about 200 lines and it is
the only file that knows both sides.
