# Every baked vine stood in a row on flat ground and photographed: what the
# Vines dock, the checks and the bake made, side by side. Development tooling;
# not part of the game. Needs a window (not --headless).
#
#   Godot..._console.exe --path . --script scripts/debug/vinelook.gd -- perflogs/vines.png
#   Godot..._console.exe --path . --script scripts/debug/vinelook.gd -- perflogs/vines_above.png 50
#
#   Godot..._console.exe --path . --script scripts/debug/vinelook.gd -- perflogs/leaves.png leaves
#
#   Godot..._console.exe --path . --script scripts/debug/vinelook.gd -- perflogs/close.png close prp_vine_fruiting_4532344 30,90,200
#
# The number is the camera's elevation in degrees (default 6: eye height);
# `leaves` lays every kind's leaf shapes out instead, a row a kind, from above;
# `close` stands one vine alone and photographs it from the ground, looking up
# into its canopy, once from each distance in metres (close_30.png, ...):
# either side of its coarse copies' distances, are the leaves on the vine?
# A fifth number looks down from that many degrees up instead: its shadow.
extends SceneTree

const SPACING := 80.0

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	var out: String = args[0] if args.size() > 0 else "perflogs/vines.png"
	var sheet := args.size() > 1 and args[1] == "leaves"
	var elevation: float = float(args[1]) if args.size() > 1 and not sheet else 6.0
	var stage := Node3D.new()
	root.add_child(stage)

	var env := Environment.new()
	env.background_mode = Environment.BG_COLOR
	env.background_color = Color(0.62, 0.74, 0.86)
	env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.ambient_light_color = Color(0.55, 0.6, 0.65)
	var world := WorldEnvironment.new()
	world.environment = env
	stage.add_child(world)
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-50, 30, 0)
	sun.shadow_enabled = true
	# As every level has it.
	sun.directional_shadow_max_distance = 140.0
	stage.add_child(sun)
	var ground := MeshInstance3D.new()
	var plane := PlaneMesh.new()
	plane.size = Vector2(2000, 2000)
	ground.mesh = plane
	var dirt := StandardMaterial3D.new()
	dirt.albedo_color = Color(0.42, 0.36, 0.28)
	ground.material_override = dirt
	stage.add_child(ground)

	if args.size() > 3 and args[1] == "close":
		await _close(stage, out, args[2], args[3], float(args[4]) if args.size() > 4 else -1.0)
		return
	if sheet:
		var check = load("res://scripts/debug/VineCheck.cs").new()
		root.add_child(check)
		stage.add_child(check.call("LeafSheet"))
		var top := Camera3D.new()
		top.fov = 50
		stage.add_child(top)
		top.look_at_from_position(Vector3(0, 75, 22), Vector3(0, 0, 0))
		await _shoot(out)
		return
	var files := DirAccess.get_files_at("res://scenes/props")
	var names: Array[String] = []
	for f in files:
		if f.begins_with("prp_vine_") and f.ends_with(".tscn") and RegEx.create_from_string("^prp_vine_[a-z]+_[0-9]+c?\\.tscn$").search(f):
			names.append(f)
	names.sort()
	var width: float = SPACING * maxi(1, names.size() - 1)
	for i in names.size():
		var vine: Node3D = load("res://scenes/props/" + names[i]).instantiate()
		vine.position = Vector3(i * SPACING - width * 0.5, 0, 0)
		stage.add_child(vine)
		var label := Label3D.new()
		label.text = names[i].get_basename().trim_prefix("prp_vine_")
		label.font_size = 400
		label.pixel_size = 0.02
		label.position = vine.position + Vector3(0, 4, 30)
		label.rotation_degrees = Vector3(-30, 0, 0)
		stage.add_child(label)

	var camera := Camera3D.new()
	camera.fov = 50
	stage.add_child(camera)
	var target := Vector3(0, 45, 0)
	var distance := width * 0.5 + 90.0
	var eye := target + Vector3(0, sin(deg_to_rad(elevation)), cos(deg_to_rad(elevation))) * distance
	camera.look_at_from_position(eye, target)

	await _shoot(out)

func _close(stage: Node3D, out: String, vine_name: String, distances: String, elevation: float) -> void:
	var vine: Node3D = load("res://scenes/props/%s.tscn" % vine_name).instantiate()
	stage.add_child(vine)
	var camera := Camera3D.new()
	camera.fov = 50
	camera.far = 4000
	stage.add_child(camera)
	for i in 30:
		await process_frame
	var top := 0.0
	var leaves = vine.get_node_or_null("Leaves")
	if leaves != null and leaves.get("Set") != null:
		for c in leaves.get("Set").get("Centre"):
			top = maxf(top, c.y)
	var target := Vector3(0, top * 0.7, 0)
	for d in distances.split(","):
		var metres := float(d)
		if elevation >= 0.0:
			# From above, at the vine's foot and middle: its shadow on the ground.
			var eye := Vector3(0, sin(deg_to_rad(elevation)), cos(deg_to_rad(elevation))) * metres
			camera.look_at_from_position(eye, Vector3(0, top * 0.3, 0))
		else:
			camera.look_at_from_position(Vector3(metres * 0.3, 2.0, metres), target)
		await _shoot(out.get_basename() + "_" + d + "." + out.get_extension(), false)
	quit(0)

func _shoot(out: String, done := true) -> void:
	for i in 30:
		await process_frame
	var image := root.get_viewport().get_texture().get_image()
	var path := ProjectSettings.globalize_path("res://" + out) if not out.is_absolute_path() else out
	DirAccess.make_dir_recursive_absolute(path.get_base_dir())
	image.save_png(path)
	print("[vinelook] -> ", path)
	if done:
		quit(0)
