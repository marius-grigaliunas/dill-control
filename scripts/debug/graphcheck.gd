# Headless check of the chunk graph against a flood of cells (decision 0027).
# Development tooling; not part of the game.
#
#   Godot..._console.exe --headless --path . --script scripts/debug/graphcheck.gd -- [hits]
#
# Prints PASS or FAIL and a line per model; exits 1 on FAIL.
extends SceneTree

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	var hits := int(args[0]) if args.size() > 0 else 40
	var scenes := PackedStringArray()
	for file in DirAccess.get_files_at("res://scenes/props"):
		if file.ends_with(".tscn") and (file.begins_with("prp_graybox") or file.begins_with("prp_tree") or file.begins_with("prp_jar") or file.begins_with("prp_vine")):
			scenes.append("res://scenes/props/" + file)
	var check = ClassDB.instantiate("GraphCheck") if ClassDB.class_exists("GraphCheck") else load("res://scripts/debug/GraphCheck.cs").new()
	root.add_child(check)
	var report = check.call("Run", scenes, hits)
	if report == null:
		quit(1)
		return
	print(report)
	quit(0 if report.begins_with("PASS") else 1)
