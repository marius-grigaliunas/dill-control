# Headless determinism and seam check. Development tooling; not part of the game.
#
#   Godot..._console.exe --headless --path . --script scripts/debug/seedcheck.gd -- [seeds]
#
# Prints PASS or FAIL and a line per seed; exits 1 on FAIL.
extends SceneTree

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	var seeds := int(args[0]) if args.size() > 0 else 20
	var check = ClassDB.instantiate("SeedCheck") if ClassDB.class_exists("SeedCheck") else load("res://scripts/debug/SeedCheck.cs").new()
	root.add_child(check)
	var report: String = check.call("Run", seeds)
	print(report)
	quit(0 if report.begins_with("PASS") else 1)
