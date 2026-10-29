extends SceneTree

# lvl_tutorial's opening from the player's own eyes: the pod's fall (the window
# view and the prompt), the landing, and stepping out. Saved to perflogs/.
#   "$GODOT" --path . --script scripts/debug/tutorialshots.gd

const LEVEL := "res://scenes/levels/lvl_tutorial.tscn"
const SHOTS := [[3.0, "tut_fall_a"], [7.0, "tut_fall_b"], [10.5, "tut_fall_c"], [13.0, "tut_landed"], [16.5, "tut_out"], [19.0, "tut_walk"]]

var _t := 0.0
var _next := 0

func _initialize() -> void:
	var level: Node = load(LEVEL).instantiate()
	root.add_child(level)
	# Effects (a landing's blast, a rocket) are added to the current scene.
	current_scene = level

func _process(delta: float) -> bool:
	_t += delta
	if _next < SHOTS.size() and _t >= SHOTS[_next][0]:
		var image := root.get_viewport().get_texture().get_image()
		image.save_png("res://perflogs/%s.png" % SHOTS[_next][1])
		print("[shot] ", SHOTS[_next][1], " at ", snappedf(_t, 0.1))
		_next += 1
	if _t > 20.0:
		quit()
	return false
