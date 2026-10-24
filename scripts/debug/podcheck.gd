# Headless check of a nest's pods (PodField). Development tooling; not part of the game.
#
#   Godot..._console.exe --headless --path . --script scripts/debug/podcheck.gd [-- pop=3000 nest=SEED]
#
# Loads lvl_nest_lab, then: every vine's pods are in the field, every pod
# hangs from a voxel, and pods bend with their vine. Then it craters the
# heart's vine out from under it and waits: the felled vine's pods must be
# destroyed, and only those near it. Prints PASS or FAIL; exits 1 on FAIL.
extends SceneTree

const LEVEL := "res://scenes/levels/lvl_nest_lab.tscn"

var _failed := false

func _check(name: String, ok: bool, detail: String = "") -> void:
	print("  %s %s  %s" % ["ok  " if ok else "FAIL", name, detail])
	if not ok:
		_failed = true

func _initialize() -> void:
	_run.call_deferred()

func _run() -> void:
	await process_frame
	change_scene_to_file(LEVEL)
	await process_frame
	await process_frame
	var started := Time.get_ticks_msec()
	while current_scene == null or current_scene.Loading:
		if Time.get_ticks_msec() - started > 240000:
			print("FAIL: the nest did not load in 240 s")
			quit(1)
			return
		await process_frame
	for i in 30:
		await physics_frame
	var level := current_scene
	var builder: Node = level.get_node("Geometry/NestBuilder")
	var pods: Node = level.get_node("Props/PodField")
	print(pods.Summary())
	var layout_pods: int = builder.PlannedPods
	_check("every vine's pods are in the field", pods.Count == layout_pods and pods.Vines == builder.Planted,
		"%d pods on %d vines, the plan %d" % [pods.Count, pods.Vines, layout_pods])
	_check("every pod is full and none destroyed", pods.Full == pods.Count and pods.Destroyed == 0)
	var unheld := 0
	var moved := 0
	for i in pods.Count:
		if pods.Built(i).distance_to(pods.Drawn(i)) > 0.01:
			moved += 1
	var summary: String = pods.Summary()
	unheld = int(summary.get_slice("; ", 1).get_slice(" ", 0))
	_check("every pod hangs from a voxel", unheld == 0, "%d hang from none" % unheld)
	_check("pods bend with their vine in the wind", moved > pods.Count / 2, "%d of %d moved off where they were built" % [moved, pods.Count])

	# Craters the heart's vine out from under it: it falls, and its pods with it.
	var heart: Vector3 = builder.BenchHeart("0")
	var up := heart.normalized()
	var heart_vine := -1
	var closest := INF
	for i in pods.Count:
		var across: float = (pods.Built(i) - heart).slide(up).length()
		if across < closest:
			closest = across
			heart_vine = pods.VineOf(i)
	var near := 0
	for i in pods.Count:
		if pods.VineOf(i) == heart_vine:
			near += 1
	builder.BenchCraterHeart()
	var waited := 0.0
	while waited < 20.0 and pods.Destroyed < near / 2:
		await physics_frame
		waited += 1.0 / Engine.physics_ticks_per_second
	for i in 60:
		await physics_frame
	print(pods.Summary())
	var far_lost := 0
	for i in pods.Count:
		if pods.StateOf(i) == 3 and (pods.Built(i) - heart).slide(up).length() > 200.0:
			far_lost += 1
	_check("the felled vine's pods are destroyed", pods.Destroyed >= near / 2,
		"%d destroyed after %.1f s, the heart vine has %d" % [pods.Destroyed, waited, near])
	_check("no pod far from it is", far_lost == 0, "%d more than 200 m off" % far_lost)
	print("FAIL" if _failed else "PASS")
	quit(1 if _failed else 0)
