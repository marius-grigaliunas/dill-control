# Headless check of the player's shove cap and fling guard. Development tooling; not part of the game.
#
#   Godot..._console.exe --headless --path . --script scripts/debug/flingcheck.gd
#
# Loads lvl_horde_flats with no horde, then: three 200 m/s shoves in one tick
# move the player no faster than MovementTuning.MaxShoveSpeed, and are not a
# fling; the player moved 100 m between ticks is reported as one; so is a
# player moving at 100 m/s. Prints PASS or FAIL; exits 1 on FAIL.
extends SceneTree

const LEVEL := "res://scenes/levels/lvl_horde_flats.tscn"

var _failed := false

func _check(name: String, ok: bool, detail: String = "") -> void:
	print("  %s %s  %s" % ["ok  " if ok else "FAIL", name, detail])
	if not ok:
		_failed = true

func _initialize() -> void:
	_run.call_deferred()

func _ticks(n: int) -> void:
	for i in n:
		await physics_frame

func _run() -> void:
	await process_frame
	change_scene_to_file(LEVEL)
	await process_frame
	await process_frame
	var level := current_scene
	var horde := level.find_child("Horde", true, false)
	if horde != null:
		horde.queue_free()
	var started := Time.get_ticks_msec()
	while level.Player == null:
		if Time.get_ticks_msec() - started > 60000:
			print("FAIL: no player in 60 s")
			quit(1)
			return
		await process_frame
	var player: CharacterBody3D = level.Player
	await _ticks(60)
	var cap: float = player.Tuning.MaxShoveSpeed

	for i in 3:
		player.ApplyBlastImpulse(Vector3(200.0, 0.0, 0.0))
	await _ticks(1)
	var speed := player.velocity.length()
	_check("three 200 m/s shoves in a tick are capped", speed <= cap + 2.0, "%.1f m/s, cap %.0f" % [speed, cap])
	_check("a capped shove is not a fling", player.Flings == 0, "%d reported" % player.Flings)

	await _ticks(90)
	var before: int = player.Flings
	player.global_position += Vector3(100.0, 0.0, 0.0)
	await _ticks(2)
	_check("moved 100 m between ticks is reported", player.Flings == before + 1, "%d reported" % (player.Flings - before))

	await _ticks(90)
	before = player.Flings
	player.velocity = Vector3(0.0, 100.0, 0.0)
	await _ticks(2)
	_check("moving at 100 m/s is reported", player.Flings == before + 1, "%d reported" % (player.Flings - before))

	print("PASS" if not _failed else "FAIL")
	quit(1 if _failed else 0)
