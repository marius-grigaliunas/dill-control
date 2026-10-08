# Unattended benchmark harness. Development tooling; not part of the game.
#
# Usage, from the project folder:
#   Godot..._console.exe --path . --script scripts/debug/bench.gd
#   Godot..._console.exe --path . --script scripts/debug/bench.gd -- <res://scene.tscn>
#   Godot..._console.exe --path . --script scripts/debug/bench.gd -- <res://scene.tscn> <mode>
#
# Modes: idle (the default), look (screenshots of the view, for checking art
# and shaders), ink (the ink pass on, off and on again).
#
# It instances the level itself and sets current_scene, so effects that parent
# to the scene behave as they do on a normal launch. Vsync goes off so the frame
# rate is the real one rather than the 60 the swap chain would impose.
#
# A run also compiles every shader the level uses against the real driver, so
# it doubles as the check that a shader edit did not break anything. --headless
# does NOT do that: the dummy renderer never parses them.
#
# Modes are added with the systems they measure. Ported from the lab
# (fps-swampy), whose bench.gd has the full set to copy from.
extends SceneTree

# Measurement starts when the scene stops changing, not on a clock. In the lab a
# fixed warm-up silently measured a half-built planet whenever the machine was
# busy; waiting for the draw count to hold still removed that.
const SETTLE_TOLERANCE := 0.06   # fractional drift allowed in the draw count
const SETTLE_HOLD := 2.0         # seconds it has to stay within that
const SETTLE_MIN := 6.0          # never measure before this
const WARMUP_MAX := 40.0         # give up and measure anyway
const STEP := 4.0                # default phase length
const BUDGET := 1.0 / 60.0

# Phases: [label, actions on entry, actions held for the phase, optional length].
# An action is an input action name, or one of:
#   "#name"                  save the screen to perflogs/name.png. The capture
#                            costs 100-150 ms itself, so a phase that takes one
#                            reports it as its worst frame; time a separate phase.
#   "!hide:Node" "!show:Node"  flip the first node of that name, for an A/B
#   "!set:Node:prop=value"   set a property, value parsed as a Godot literal
const IDLE_TIMELINE := [
	["idle", [], [], 6.0],
]

const LOOK_TIMELINE := [
	["look", ["#look"], [], 1.0],
	["look, no ink", ["!hide:ToonPass"], [], 0.5],
	["look, no ink shot", ["#look_noink"], [], 0.5],
	["ink back", ["!show:ToonPass"], [], 0.5],
]

const INK_TIMELINE := [
	["ink on", [], [], 5.0],
	["ink off", ["!hide:ToonPass"], [], 5.0],
	["ink on again", ["!show:ToonPass"], [], 5.0],
	["ink off again", ["!hide:ToonPass"], [], 5.0],
]

const TARGET_SIZE := Vector2i(1920, 1080)

var _elapsed := 0.0
var _phase := -1
var _phase_started := 0.0
var _vp: RID
var _timeline: Array = []
var _held: Array[String] = []

var _settled := false
var _steady_draws := 0.0
var _steady_since := 0.0

# Per phase. The worst frame and the count of frames over budget are the two
# numbers that answer "does it ever miss 60"; a smoothed or sampled average is
# built to hide exactly the frame worth finding.
var _worst := 0.0
var _total := 0.0
var _frames := 0
var _over := 0
var _gpu_total := 0.0

func _initialize() -> void:
	DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
	_force_target_resolution()

	var args := OS.get_cmdline_user_args()
	var path: String = args[0] if args.size() > 0 else ProjectSettings.get_setting("application/run/main_scene")
	var mode: String = args[1] if args.size() > 1 else "idle"

	match mode:
		"look": _timeline = LOOK_TIMELINE
		"ink": _timeline = INK_TIMELINE
		_: _timeline = IDLE_TIMELINE

	print("=== bench: ", path, "  mode=", mode, "  window ", DisplayServer.window_get_size(),
		"  vsync=", DisplayServer.window_get_vsync_mode(), " (0=disabled) ===")

	var level: Node = load(path).instantiate()
	root.add_child(level)
	current_scene = level

	_vp = root.get_viewport_rid()
	RenderingServer.viewport_set_measure_render_time(_vp, true)

# The requirement is 1920x1080 native. On a 1920x1080 screen a window that size
# cannot fit beside its title bar and the taskbar, and Windows shrinks it, so the
# bench goes fullscreen there; on anything bigger it asks for the exact window.
# Every report line prints the size it actually got.
func _force_target_resolution() -> void:
	var screen := DisplayServer.screen_get_size()
	if screen.x <= TARGET_SIZE.x or screen.y <= TARGET_SIZE.y:
		DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_FULLSCREEN)
	else:
		DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_WINDOWED)
		DisplayServer.window_set_size(TARGET_SIZE)
		DisplayServer.window_set_position((screen - TARGET_SIZE) / 2)

func _phase_length(entry: Array) -> float:
	return entry[3] if entry.size() > 3 else STEP

func _tap(action: String) -> void:
	if action.begins_with("#"):
		var image := root.get_viewport().get_texture().get_image()
		var dir := ProjectSettings.globalize_path("res://perflogs")
		DirAccess.make_dir_recursive_absolute(dir)
		if not FileAccess.file_exists(dir + "/.gdignore"):
			FileAccess.open(dir + "/.gdignore", FileAccess.WRITE)
		var file := "%s/%s.png" % [dir, action.substr(1)]
		image.save_png(file)
		print("[bench] saved ", file)
		return
	if action.begins_with("!hide:") or action.begins_with("!show:"):
		var target: Node = null
		for candidate in root.find_children(action.substr(6), "", true, false):
			if "visible" in candidate:
				target = candidate
				break
		if target != null:
			target.visible = action.begins_with("!show:")
		else:
			print("[bench] no visible node named ", action.substr(6))
		return
	if action.begins_with("!set:"):
		var parts := action.substr(5).split(":", true, 1)
		var pair := parts[1].split("=") if parts.size() == 2 else PackedStringArray()
		var node := root.find_child(parts[0], true, false)
		if node != null and pair.size() == 2:
			node.set(pair[0], str_to_var(pair[1]))
		else:
			print("[bench] cannot ", action)
		return
	_send(action, true)
	_send(action, false)

func _send(action: String, pressed: bool) -> void:
	var event := InputEventAction.new()
	event.action = action
	event.pressed = pressed
	Input.parse_input_event(event)
	# Weapons poll Input rather than consuming events, so the polled state has
	# to be set too or a held trigger never fires.
	if pressed:
		Input.action_press(action)
	else:
		Input.action_release(action)

func _report(label: String) -> void:
	var size := DisplayServer.window_get_size()
	if _frames == 0:
		print("[bench] %-24s no frames" % label)
		return
	print("[bench] %-24s mean=%6.2f worst=%7.2f over16.7=%3d/%4d  gpu mean=%5.2f  draws=%d  %dx%d" % [
		label, _total / _frames * 1000.0, _worst * 1000.0, _over, _frames,
		_gpu_total / _frames,
		Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME),
		size.x, size.y,
	])
	# Where the CPU side of the frame went, from PhysicsStepProbe: the engine's
	# physics step, every script's physics and process callbacks, and the rest
	# (render and wait). Godot's TIME_PROCESS monitor is not used: read from
	# here it reported 114-381 ms a frame against 9-11 ms frames.
	var driver := root.find_child("PerfDriver", true, false)
	if driver != null:
		print("[bench]   ", driver.call("TakeBenchReport"))
	_worst = 0.0
	_total = 0.0
	_frames = 0
	_over = 0
	_gpu_total = 0.0

func _process(delta: float) -> bool:
	_elapsed += delta

	if not _settled:
		var draws := Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME)
		if _steady_draws <= 0.0 or absf(draws - _steady_draws) > _steady_draws * SETTLE_TOLERANCE:
			_steady_draws = draws
			_steady_since = _elapsed
		if (_elapsed - _steady_since >= SETTLE_HOLD and _elapsed > SETTLE_MIN) or _elapsed > WARMUP_MAX:
			_settled = true
			print("=== settled after %.1fs at %d draws ===" % [_elapsed, int(draws)])
		return false

	# Every frame, not a sample of them.
	_gpu_total += RenderingServer.viewport_get_measured_render_time_gpu(_vp)
	_worst = max(_worst, delta)
	_total += delta
	_frames += 1
	if delta > BUDGET:
		_over += 1

	var want := _phase
	if _phase < 0:
		want = 0
	elif _elapsed - _phase_started >= _phase_length(_timeline[_phase]):
		want = _phase + 1
	if want >= _timeline.size():
		_report(_timeline[_phase][0])
		print("=== bench complete ===")
		return true

	if want != _phase:
		if _phase >= 0:
			_report(_timeline[_phase][0])
		var entry: Array = _timeline[want]
		# Only let go of what the next phase does not also hold: a release and
		# a press in the same frame do not cancel, so a held action would drop
		# for a frame at every phase boundary.
		var next_held: Array[String] = []
		for action in entry[2]:
			next_held.append(action)
		for action in _held:
			if not next_held.has(action):
				_send(action, false)
		_phase = want
		_phase_started = _elapsed
		for action in entry[1]:
			_tap(action)
		for action in next_held:
			if not _held.has(action):
				_send(action, true)
		_held = next_held

	return false
