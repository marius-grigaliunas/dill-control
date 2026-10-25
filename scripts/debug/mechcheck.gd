# Headless check of every mech's parts and damage map (decision 0046).
# Development tooling; not part of the game.
#
#   Godot..._console.exe --headless --path . --script scripts/debug/mechcheck.gd
#
# Prints PASS or FAIL and a block per mech; exits 1 on FAIL.
extends SceneTree

func _initialize() -> void:
	var scenes := PackedStringArray()
	for file in DirAccess.get_files_at("res://scenes/vehicles"):
		if file.begins_with("mch_") and file.ends_with(".tscn"):
			scenes.append("res://scenes/vehicles/" + file)
	var check = ClassDB.instantiate("MechCheck") if ClassDB.class_exists("MechCheck") else load("res://scripts/debug/MechCheck.cs").new()
	root.add_child(check)
	var report = check.call("Run", scenes)
	if report == null:
		quit(1)
		return
	print(report)
	quit(0 if report.begins_with("PASS") else 1)
