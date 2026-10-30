extends SceneTree

# lvl_tutorial's colony waking: a pod is harmed after landing and the pickles
# drop and hunt. Looks from the player's own eyes. Saved to perflogs/.
#   "$GODOT" --path . --script scripts/debug/wakeshots.gd

const LEVEL := "res://scenes/levels/lvl_tutorial.tscn"
const SHOTS := [[16.0, "wake_calm"], [24.0, "wake_waking"], [40.0, "wake_fight"]]

var _level: Node
var _pods: Node
var _t := 0.0
var _next := 0
var _woke := false
var _waited := 0.0

func _initialize() -> void:
	_level = load(LEVEL).instantiate()
	root.add_child(_level)
	current_scene = _level
	_pods = _level.get_node("Props/PodField")

func _process(delta: float) -> bool:
	if _level.get("Player") == null:
		_waited += delta
		if _waited > 60.0:
			quit()
		return false
	_t += delta
	if not _woke and _t >= 17.0:
		_woke = true
		_pods.call("BenchWake")
		print("[wake test] woke at ", snappedf(_t, 0.1))
	if _next < SHOTS.size() and _t >= SHOTS[_next][0]:
		root.get_viewport().get_texture().get_image().save_png("res://perflogs/%s.png" % SHOTS[_next][1])
		print("[wake test] ", SHOTS[_next][1], " at ", snappedf(_t, 0.1), "  ", _pods.call("StatusLine") if _pods.has_method("StatusLine") else "")
		_next += 1
	if _t > 42.0:
		quit()
	return false
