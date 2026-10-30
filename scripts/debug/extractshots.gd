extends SceneTree

# lvl_tutorial's way home: the contract counts as cleared, a pod is sent to a
# rolled place, the player is put at its hatch, boards, and watches the ground
# fall away. Saved to perflogs/.
#   "$GODOT" --path . --script scripts/debug/extractshots.gd

const LEVEL := "res://scenes/levels/lvl_tutorial.tscn"
# Seconds after the player comes in. The pod is sent 4 s after the clear and falls 11 s.
const STEPS := [[16.0, "clear"], [18.0, "shot:ext_counting"], [24.0, "shot:ext_inbound"], [33.0, "shot:ext_open"],
		[34.0, "board"], [38.0, "shot:ext_leaving"], [44.0, "shot:ext_climbing"], [53.0, "shot:ext_done"]]

var _level: Node
var _ext: Node
var _t := 0.0
var _next := 0
var _waited := 0.0

func _initialize() -> void:
	_level = load(LEVEL).instantiate()
	root.add_child(_level)
	current_scene = _level
	_ext = _level.get_node("Spawns/Extraction")

func _process(delta: float) -> bool:
	if _level.get("Player") == null:
		_waited += delta
		if _waited > 60.0:
			quit()
		return false
	_t += delta
	while _next < STEPS.size() and _t >= STEPS[_next][0]:
		var what: String = STEPS[_next][1]
		if what == "clear":
			_ext.call("BenchClear")
		elif what == "board":
			var player: Node3D = _level.get("Player")
			if _ext.get("HasSite"):
				var site: Vector3 = _ext.get("SiteAt")
				var up: Vector3 = site.normalized()
				var side: Vector3 = up.cross(Vector3.RIGHT).normalized()
				player.global_position = site + up * 0.6 + side * 2.4
				print("[extract test] player put at the hatch, ", snappedf(player.global_position.distance_to(site), 0.1), " m from the pod")
		else:
			root.get_viewport().get_texture().get_image().save_png("res://perflogs/%s.png" % what.substr(5))
			print("[extract test] ", what, " at ", snappedf(_t, 0.1), " phase ", _ext.get("Current"))
		_next += 1
	if _t > 55.0:
		quit()
	return false
