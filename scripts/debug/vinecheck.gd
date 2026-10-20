# Headless check of the vine generator against the browser prototype, and of
# every baked vine. Development tooling; not part of the game.
#
#   Godot..._console.exe --headless --path . --script scripts/debug/vinecheck.gd
#   Godot..._console.exe --headless --path . --script scripts/debug/vinecheck.gd -- dump <name> > ours.txt
#
# Prints PASS or FAIL and a line per check; exits 1 on FAIL. `dump` prints one
# golden vine's voxels as x y z c, to diff against `node make-golden.js dump <name>`.
extends SceneTree

func _initialize() -> void:
	var check = ClassDB.instantiate("VineCheck") if ClassDB.class_exists("VineCheck") else load("res://scripts/debug/VineCheck.cs").new()
	root.add_child(check)
	var args := OS.get_cmdline_user_args()
	if args.size() >= 2 and args[0] == "dump":
		printraw(check.call("Dump", args[1]))
		quit(0)
		return
	var report: String = check.call("Run")
	print(report)
	quit(0 if report.begins_with("PASS") else 1)
