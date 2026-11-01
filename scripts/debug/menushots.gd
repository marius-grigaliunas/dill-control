extends SceneTree

# The main menu as the game draws it, not as the editor does: the planet, the
# clouds, the air and the sky, photographed at a few times. A window opens; hands
# off while it runs. Saved to perflogs/menu_<seconds>.png.
#   "$GODOT" --path . --script scripts/debug/menushots.gd
#   "$GODOT" --path . --script scripts/debug/menushots.gd -- 1280 720 2 20 60

const SCENE := "res://scenes/ui/main_menu.tscn"

var _scene: Node
var _t := 0.0
var _times: Array = [2.0, 20.0]
var _next := 0

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	var overrides := {}
	var plain: Array = []
	var hidden: Array = []
	for a in args:
		if a.begins_with("hide:"):
			hidden.append(a.substr(5))
		elif "=" in a:
			var text: String = a.get_slice("=", 1)
			overrides[a.get_slice("=", 0)] = float(text) if "." in text else int(text)
		else:
			plain.append(a)
	if plain.size() >= 2:
		root.size = Vector2i(int(plain[0]), int(plain[1]))
	if plain.size() > 2:
		_times = []
		for i in range(2, plain.size()):
			_times.append(float(plain[i]))
	_scene = load(SCENE).instantiate()
	root.add_child(_scene)
	current_scene = _scene
	# name=value on the command line sets a shader parameter on the cloud material
	# for this run only (never saved): a quick way to try a setting without the editor.
	var clouds: MeshInstance3D = _scene.find_child("Clouds", true, false)
	for node_name in hidden:
		_scene.find_child(node_name, true, false).visible = false
	for key in overrides:
		clouds.material_override.set_shader_parameter(key, overrides[key])

func _process(delta: float) -> bool:
	_t += delta
	if _next < _times.size() and _t >= _times[_next]:
		var image := root.get_viewport().get_texture().get_image()
		var path := "res://perflogs/menu_%d.png" % int(_times[_next])
		image.save_png(path)
		print("[menu shots] ", path, " ", image.get_size())
		_next += 1
	if _next >= _times.size():
		quit()
	return false
