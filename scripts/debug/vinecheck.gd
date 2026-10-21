# Headless check of the vine generator against its recorded golden vines, and
# of every baked vine. Development tooling; not part of the game.
#
#   Godot..._console.exe --headless --path . --script scripts/debug/vinecheck.gd
#   Godot..._console.exe --headless --path . --script scripts/debug/vinecheck.gd -- record
#   Godot..._console.exe --headless --path . --script scripts/debug/vinecheck.gd -- rejections 200
#   Godot..._console.exe --headless --path . --script scripts/debug/vinecheck.gd -- settle
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
	if args.size() >= 1 and args[0] == "rejections":
		var out = check.call("Rejections", int(args[1]) if args.size() > 1 else 200)
		print(out if out is String else "FAIL: VineCheck.Rejections threw")
		quit(0 if out is String and out.begins_with("PASS") else 1)
		return
	if args.size() >= 1 and args[0] == "record":
		print(check.call("Record"))
		quit(0)
		return
	if args.size() >= 2 and args[0] == "dump":
		printraw(check.call("Dump", args[1]))
		quit(0)
		return
	if args.size() >= 1 and args[0] == "settle":
		var lines := await _settle(check)
		print(("FAIL\n" if lines.contains("FAIL") else "PASS\n") + lines)
		quit(1 if lines.contains("FAIL") else 0)
		return
	var result = check.call("Run")
	# A C# exception returns null: fail rather than hang.
	var report: String = result if result is String else "FAIL: VineCheck.Run threw (see the error above)"
	var settled := await _settle(check)
	if settled.contains("FAIL") and report.begins_with("PASS"):
		report = "FAIL" + report.substr(4)
	print(report + "Stood with Jolt, untouched:\n" + settled)
	quit(0 if report.begins_with("PASS") else 1)

# Each baked vine stood on flat ground for SETTLE_SECONDS with no wind,
# player or shots: it should not move, shed or sink (VineCheck.Stand).
const SETTLE_SECONDS := 5.0

func _settle(check: Node) -> String:
	var lines := ""
	# The root joins the tree only once _initialize has handed over.
	await process_frame
	for path in check.call("BakedVines"):
		var stage = check.call("Stand", path)
		for i in int(SETTLE_SECONDS * Engine.physics_ticks_per_second):
			await physics_frame
		lines += str(check.call("Settled", stage, SETTLE_SECONDS)) + "\n"
	return lines
