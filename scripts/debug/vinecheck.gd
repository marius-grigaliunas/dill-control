# Headless check of the vine generator against its recorded golden vines, and
# of every baked vine. Development tooling; not part of the game.
#
#   Godot..._console.exe --headless --path . --script scripts/debug/vinecheck.gd
#   Godot..._console.exe --headless --path . --script scripts/debug/vinecheck.gd -- record
#   Godot..._console.exe --headless --path . --script scripts/debug/vinecheck.gd -- dump <name> > ours.txt
#
# Prints PASS or FAIL and a line per check; exits 1 on FAIL. `dump` prints one
# golden vine's voxels as x y z c, for a diff. `record` re-records the golden
# vines: only after a change meant to move them.
extends SceneTree

func _initialize() -> void:
	var check = ClassDB.instantiate("VineCheck") if ClassDB.class_exists("VineCheck") else load("res://scripts/debug/VineCheck.cs").new()
	root.add_child(check)
	var args := OS.get_cmdline_user_args()
	if args.size() >= 1 and args[0] == "record":
		print(check.call("Record"))
		quit(0)
		return
	if args.size() >= 2 and args[0] == "dump":
		printraw(check.call("Dump", args[1]))
		quit(0)
		return
	var report: String = check.call("Run")
	print(report)
	quit(0 if report.begins_with("PASS") else 1)
