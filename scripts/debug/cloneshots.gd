extends SceneTree

# The clones in lvl_tutorial: the player is killed after landing, again in the
# next body, and again in the last (the contract is void). Saved to perflogs/.
#   "$GODOT" --path . --script scripts/debug/cloneshots.gd

const LEVEL := "res://scenes/levels/lvl_tutorial.tscn"
# Seconds after the player comes in: [time, what]. A pod falls 11 s and waits 1.8 s.
const STEPS := [[16.0, "kill"], [17.5, "shot:clone_lost"], [20.5, "shot:clone_flash"], [23.0, "shot:clone_falling"],
		[32.0, "kill"], [34.0, "shot:clone_lost2"], [48.0, "kill"], [50.0, "shot:clone_void"]]

var _level: Node
var _t := 0.0
var _next := 0
var _waited := 0.0

func _initialize() -> void:
	_level = load(LEVEL).instantiate()
	root.add_child(_level)
	current_scene = _level

func _process(delta: float) -> bool:
	if _level.get("Player") == null:
		_waited += delta
		if _waited > 60.0:
			quit()
		return false
	_t += delta
	while _next < STEPS.size() and _t >= STEPS[_next][0]:
		var what: String = STEPS[_next][1]
		if what == "kill":
			var player: Node = _level.get("Player")
			player.get("Health").call("TakeDamage", 9999.0)
			print("[clone test] killed at ", snappedf(_t, 0.1))
		else:
			var image := root.get_viewport().get_texture().get_image()
			image.save_png("res://perflogs/%s.png" % what.substr(5))
			print("[clone test] ", what, " at ", snappedf(_t, 0.1))
		_next += 1
	if _t > 52.0:
		quit()
	return false
