# Headless check of a plant's bending spine. Development tooling; not part of the game.
#
#   Godot..._console.exe --headless --path . --script scripts/debug/flexcheck.gd
#
# Prints PASS or FAIL and a few lines per kind of plant; exits 1 on FAIL.
extends SceneTree

func _initialize() -> void:
	var check = ClassDB.instantiate("FlexCheck") if ClassDB.class_exists("FlexCheck") else load("res://scripts/debug/FlexCheck.cs").new()
	root.add_child(check)
	var report: String = check.call("Run")
	print(report)
	quit(0 if report.begins_with("PASS") else 1)
