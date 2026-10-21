# Every baked vine stood in a row on flat ground and photographed: what the
# Vines dock, the checks and the bake made, side by side. Development tooling;
# not part of the game. Needs a window (not --headless).
#
#   Godot..._console.exe --path . --script scripts/debug/vinelook.gd -- perflogs/vines.png
extends SceneTree

const SPACING := 80.0

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	var out: String = args[0] if args.size() > 0 else "perflogs/vines.png"
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
	stage.add_child(sun)
	var ground := MeshInstance3D.new()
	var plane := PlaneMesh.new()
	plane.size = Vector2(2000, 2000)
	ground.mesh = plane
	var dirt := StandardMaterial3D.new()
	dirt.albedo_color = Color(0.42, 0.36, 0.28)
	ground.material_override = dirt
	stage.add_child(ground)

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
	camera.look_at_from_position(Vector3(0, 55, width * 0.5 + 90), Vector3(0, 45, 0))

	for i in 30:
		await process_frame
	var image := root.get_viewport().get_texture().get_image()
	var path := ProjectSettings.globalize_path("res://" + out) if not out.is_absolute_path() else out
	DirAccess.make_dir_recursive_absolute(path.get_base_dir())
	image.save_png(path)
	print("[vinelook] ", names.size(), " vines -> ", path)
	quit(0)
