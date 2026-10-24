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
	unheld = pods.Unheld()
	_check("every pod hangs from a voxel", unheld == 0, "%d hang from none" % unheld)
	_check("pods bend with their vine in the wind", moved > pods.Count / 2, "%d of %d moved off where they were built" % [moved, pods.Count])

	# A shot, as a hitscan weapon casts one, at a pickle a proxy stands on
	# (the pickles nearest the camera's aim): from 8 m to its side, where the
	# player's view may be a hill, handed to the Hitbox the ray meets.
	var camera := root.get_viewport().get_camera_3d()
	var space := camera.get_world_3d().direct_space_state
	var shot_pod := -1
	var assigned := 0
	for proxy in pods.get_children():
		if not proxy.has_method("Release") or proxy.Pod < 0:
			continue
		assigned += 1
		if shot_pod >= 0:
			continue
		var pod: int = proxy.Pod
		var middle: Vector3 = pods.Drawn(pod) - pods.Drawn(pod).normalized() * 0.9
		var up := middle.normalized()
		var from := middle + up.cross(Vector3.FORWARD).normalized() * 8.0
		var query := PhysicsRayQueryParameters3D.create(from, middle + (middle - from).normalized() * 2.0, 0xFFFFFFFF)
		query.collide_with_areas = true
		var hit := space.intersect_ray(query)
		if hit.is_empty() or not hit.collider.has_method("TakeHit") or hit.collider.get_parent() != proxy:
			if OS.get_cmdline_user_args().has("debug"):
				print("    pod %d %.0f m off: %s" % [pod, from.distance_to(middle), "nothing" if hit.is_empty() else "%s at %.0f m" % [hit.collider.name, from.distance_to(hit.position)]])
			continue
		var info = load("res://scripts/core/DamageInfo.cs").new()
		info.Amount = 1000.0
		info.Position = hit.position
		info.Direction = (middle - from).normalized()
		hit.collider.TakeHit(info)
		shot_pod = pod
	_check("proxies stand on the pickles nearest the aim", assigned > 0, "%d assigned" % assigned)
	_check("a shot pickle drops dead and leaves its pod empty", shot_pod >= 0 and pods.StateOf(shot_pod) == 1 and pods.Killed == 1,
		"pod %d, %d killed" % [shot_pod, pods.Killed])

	# A blast on the bus, 6 m round a pickle: every pickle in it, none past it.
	var target: int = pods.Count - 1
	var blast = load("res://scripts/core/BlastInfo.cs").new()
	blast.Centre = pods.Drawn(target)
	blast.Radius = 6.0
	blast.InnerRadius = 2.0
	blast.Damage = 500.0
	blast.MinMultiplier = 0.5
	var inside := 0
	for i in pods.Count:
		if pods.StateOf(i) == 0 and pods.Drawn(i).distance_to(blast.Centre) <= 6.0:
			inside += 1
	var before: int = pods.Killed
	root.get_node("EventBus").emit_signal("AreaBlast", blast)
	_check("a blast kills every pickle in it and none past it", pods.Killed - before == inside and inside > 0,
		"%d killed, %d inside" % [pods.Killed - before, inside])
	# The shot woke the colony: over WakeSeconds every full pod's pickle
	# drops into the horde, the nearest the shot first.
	_check("a shot on a hanging pickle wakes the colony", pods.Colony == 1, "colony %d" % pods.Colony)
	var horde: Node = level.get_node_or_null("Spawns/Horde")
	var wake_started := Time.get_ticks_msec()
	var first_dropped := -1
	while pods.Colony != 2 and Time.get_ticks_msec() - wake_started < 60000:
		await physics_frame
		if first_dropped < 0 and pods.Dropped > 0:
			first_dropped = Time.get_ticks_msec() - wake_started
	var woke_in := (Time.get_ticks_msec() - wake_started) / 1000.0
	print(pods.Summary())
	_check("over WakeSeconds every pickle left drops", pods.Colony == 2 and pods.Full == 0,
		"awake after %.1f s, %d still full, %d dropped" % [woke_in, pods.Full, pods.Dropped])
	# Its throwers' cornichons are agents of another origin.
	_check("into the horde, as pod pickles", horde != null and horde.Manager.ExtraCount(5) == pods.Dropped,
		"%d in the horde from pods, %d agents in all" % [horde.Manager.ExtraCount(5) if horde else -1, horde.Manager.AgentCount if horde else -1])

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
