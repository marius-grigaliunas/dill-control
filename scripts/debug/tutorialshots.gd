extends SceneTree

# lvl_tutorial's opening from the player's own eyes: the pod's fall (the window
# view and the prompt), the landing, and stepping out. Saved to perflogs/. The
# clock starts when the loading screen lets the player in.
#   "$GODOT" --path . --script scripts/debug/tutorialshots.gd

const LEVEL := "res://scenes/levels/lvl_tutorial.tscn"
const SHOTS := [[0.5, "tut_flash"], [3.0, "tut_fall_a"], [7.0, "tut_fall_b"], [10.5, "tut_fall_c"], [12.0, "tut_landed"], [15.0, "tut_out"], [19.0, "tut_walk"]]

var _level: Node
var _t := 0.0
var _next := 0
var _waited := 0.0

func _initialize() -> void:
	_level = load(LEVEL).instantiate()
	root.add_child(_level)
	# Effects (a landing's blast, a rocket) are added to the current scene.
	current_scene = _level

func _process(delta: float) -> bool:
	if _level.get("Player") == null:
		_waited += delta
		if _waited > 60.0:
			print("[shot] the player never came in")
			quit()
		return false
	_t += delta
	if _next < SHOTS.size() and _t >= SHOTS[_next][0]:
		var image := root.get_viewport().get_texture().get_image()
		image.save_png("res://perflogs/%s.png" % SHOTS[_next][1])
		print("[shot] ", SHOTS[_next][1], " at ", snappedf(_t, 0.1))
		_next += 1
	if _t > 21.0:
		quit()
	return false
