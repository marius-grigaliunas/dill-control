# Bakes one vine headless, as the vine dock's Bake does. Development tooling;
# not part of the game.
#
#   Godot..._console.exe --headless --path . --script scripts/debug/vinebake.gd -- fruiting pool:0
#   Godot..._console.exe --headless --path . --script scripts/debug/vinebake.gd -- leafy 7103694
#   Godot..._console.exe --headless --path . --editor --quit     # imports what it wrote
#
# Exits 1 if the vine fails its hard checks and nothing was written.
extends SceneTree

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() < 2:
		print("usage: vinebake.gd -- <kind> <seed | pool:N>")
		quit(1)
		return
	var bake = ClassDB.instantiate("VineBake") if ClassDB.class_exists("VineBake") else load("res://scripts/debug/VineBake.cs").new()
	root.add_child(bake)
	var report: String = bake.call("Run", args[0], args[1])
	print(report)
	quit(0 if report.begins_with("OK") else 1)
