# Unattended benchmark harness. Development tooling; not part of the game.
#
# Usage, from the project folder:
#   Godot..._console.exe --path . --script scripts/debug/bench.gd
#   Godot..._console.exe --path . --script scripts/debug/bench.gd -- <res://scene.tscn>
#   Godot..._console.exe --path . --script scripts/debug/bench.gd -- <res://scene.tscn> <mode>
#
# Modes: idle (the default), look (screenshots of the view, for checking art
# and shaders), ink (the ink pass on, off and on again), grapple (the line
# thrown, reeled, swung and zipped against lvl_horde_flats' compound), horde
# [count] (the crowd seeded, arriving and on top of the player), mech (the ally
# mech followed, ridden, jumped and left), bike (the hoverbike flown and every
# weapon fired), both in lvl_horde_flats.
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
const PULSE_SECONDS := 0.15

# Phases: [label, actions on entry, actions held for the phase, optional length].
# A held action written "~fire" is tapped every PULSE_SECONDS instead, for a
# weapon that fires on the press rather than while held.
# An action is an input action name, or one of:
#   "#name"                  save the screen to perflogs/name.png. The capture
#                            costs 100-150 ms itself, so a phase that takes one
#                            reports it as its worst frame; time a separate phase.
#   "!hide:Node" "!show:Node"  flip the first node of that name, for an A/B
#   "!set:Node:prop=value"   set a property, value parsed as a Godot literal. The
#                            property may be an indexed path into a resource,
#                            "!set:Horde:Stats:AgentCount=25000"
#   "!call:Node:method"      call a method with no arguments
#   "@aim:Node:height"       turn the player to look at that node, raised by
#                            height metres
#   "@status"                toggle a line of the player's and the horde's state
#                            twice a second
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

# The line against lvl_horde_flats' compound. With @status on, the rope state
# is printed twice a second, which is what this mode is for: the frame numbers
# matter less than whether the line attaches, hauls, holds a speed cap and lets
# go. Ported from the lab's grapple mode, aimed at authored nodes by name.
const GRAPPLE_TIMELINE := [
	["aim at a pillar", ["@status", "@aim:Pillar6:24"], [], 1.0],
	["throw", [], ["grapple"], 1.0],
	["reel, half way", [], ["grapple", "jump"], 0.9],
	["let go mid-haul, carrying", [], [], 3.0],
	["aim at a column", ["@aim:Column1:18"], [], 0.8],
	["throw", [], ["grapple"], 1.0],
	["reel to the top of the line", [], ["grapple", "jump"], 3.0],
	["swing, pumping forward", [], ["grapple", "move_forward"], 3.0],
	["swing, pumping back", [], ["grapple", "move_back"], 3.0],
	["released", [], [], 1.5],
	["aim for a tap zip", ["@aim:Pillar1:28"], [], 0.6],
	["tap zip", [], ["grapple"], 0.12],
	["zipping", [], [], 2.0],
	["jump lets go, apex float", ["jump"], [], 1.5],
	["hover", ["fly_toggle"], [], 2.0],
	["dash", ["sprint"], [], 1.0],
	["falling", ["fly_toggle"], [], 2.5],
	["shot", ["#grapple_end"], [], 0.5],
]

# The ally mech in lvl_horde_flats: it walks to its spot beside the player,
# is climbed on, walked forward, sideways and back, turned by the camera,
# jumped, jetted, seen from the seat and climbed off. Screenshots at each, and
# the mech's state printed. The frame numbers include the horde arriving.
const MECH_STATUS := "!call:Companion:PrintBenchStatus"
const MECH_TIMELINE := [
	["look at the tank", ["@aim:QuadrupedTank:2.5", MECH_STATUS], [], 0.5],
	["shot", ["#mech_start"], [], 0.3],
	["it walks to its spot", [MECH_STATUS], [], 1.0],
	["watching it walk", ["@aim:QuadrupedTank:2.5", MECH_STATUS], [], 0.3],
	["walking shot", ["#mech_follow"], [], 0.3],
	["arrived", [MECH_STATUS], [], 3.0],
	["walk to it", ["@aim:QuadrupedTank:0", MECH_STATUS], ["move_forward"], 1.0],
	["climb on", ["interact"], [], 1.0],
	["forward", [MECH_STATUS], ["move_forward"], 2.0],
	["forward shot", ["#mech_ride", MECH_STATUS], ["move_forward"], 0.3],
	["strafe right", [], ["move_right"], 2.0],
	["strafe shot", ["#mech_strafe", MECH_STATUS], ["move_right"], 0.3],
	["backward", [], ["move_back"], 1.5],
	["camera swung, walking", ["!call:Companion:BenchTurnCamera", MECH_STATUS], ["move_forward"], 0.5],
	["turning shot", ["#mech_turn", MECH_STATUS], ["move_forward"], 0.3],
	["walking on", [], ["move_forward"], 1.0],
	["stop", [MECH_STATUS], [], 1.5],
	["jump", ["jump"], [], 0.45],
	["jump shot", ["#mech_jump", MECH_STATUS], [], 0.2],
	["jets", [], ["jump"], 1.2],
	["jets shot", ["#mech_jets", MECH_STATUS], ["jump"], 0.2],
	["falling", [], [], 2.5],
	["landed", [MECH_STATUS], [], 0.5],
	["seat view", ["view_toggle"], [], 0.5],
	["seat shot", ["#mech_seat"], [], 0.3],
	["seat, walking", [], ["move_forward"], 2.0],
	["climb off", ["interact"], [], 1.0],
	["looking back", ["@aim:QuadrupedTank:2.5", MECH_STATUS], [], 0.4],
	["off shot", ["#mech_off"], [], 0.3],
]

# The hoverbike in lvl_horde_flats: boarded, flown out in both modes, every
# weapon fired toward the crowd, landed and left. Its state comes from the
# player's line (stowed, so it reads where they boarded) and screenshots.
const BIKE_TIMELINE := [
	["board", ["@status", "!call:Systems:BoardNow"], [], 1.0],
	["climb", [], ["jump"], 1.0],
	["hover forward", [], ["move_forward"], 2.0],
	["hover shot", ["#bike_hover"], ["move_forward"], 0.3],
	["cruise, boosting", ["ship_mode_toggle"], ["move_forward", "sprint"], 3.0],
	["cruise shot", ["#bike_cruise"], ["move_forward"], 0.3],
	["hover again", ["ship_mode_toggle"], [], 2.0],
	["gun", ["slot_1"], ["fire"], 3.0],
	["gun shot", ["#bike_gun"], ["fire"], 0.3],
	["laser", ["slot_2"], ["fire"], 3.0],
	["laser shot", ["#bike_laser"], ["fire"], 0.3],
	["pod", [], ["alt_fire"], 2.0],
	["pod shot", ["#bike_pod"], [], 0.3],
	["bombs", [], ["~grenade"], 3.0],
	["roll", [], ["ship_roll_right"], 1.0],
	["descend", [], ["crouch"], 2.5],
	["leave", ["!call:Systems:LeaveNow"], [], 1.5],
	["off shot", ["#bike_off"], [], 0.3],
]

# The horde coming for the player at a given count, then held on top of them.
# Seeded on a tighter ring than it ships with, so the crowd arrives inside a
# run rather than after two minutes; the ring goes back before the crowd phase
# so respawns land where they would in play. Then fought with every gun, the
# load held constant by respawning the dead. Ported from the lab's horde mode.
static func _horde_timeline(count: int) -> Array:
	return [
		["seed %d" % count, ["@status", "!set:Horde:Stats:HuntSeedMinRadius=30.0",
			"!set:Horde:Stats:HuntSeedRadius=150.0", "!set:Horde:Stats:AgentCount=%d" % count], [], 3.0],
		["reseed", ["!call:Horde:BenchReseed"], [], 12.0],
		["arriving", ["perf_record"], [], 12.0],
		["crowd on you", ["!set:Horde:Stats:HuntSeedMinRadius=150.0", "!set:Horde:Stats:HuntSeedRadius=450.0"], [], 10.0],
		["stop recording", ["perf_record"], [], 0.5],
		["rifle into it", ["slot_1", "@aim:Horde:0.5"], ["fire"], 6.0],
		["machete", ["slot_2"], ["~fire"], 5.0],
		["rockets", ["slot_3"], ["~fire"], 3.0],
		["rockets shot", ["#hud_rockets"], ["~fire"], 0.5],
		["rockets, late", [], ["~fire"], 2.5],
		["shotgun", ["slot_4"], ["~fire"], 5.0],
		["grenades", ["slot_1"], ["grenade"], 5.0],
		["hud shot", ["#hud_fight"], ["grenade"], 0.5],
		["holding ground", [], [], 5.0],
		["crowd, looking down", ["@aim:Horde:-40"], [], 4.0],
		["shot", ["#horde_crowd"], [], 0.5],
		["frustum cull off", ["!set:Horde:Stats:FrustumCull=false"], [], 4.0],
		["frustum cull on", ["!set:Horde:Stats:FrustumCull=true"], [], 4.0],
		["low detail off", ["!set:Horde:Stats:LodDistance=200.0"], [], 4.0],
		["low detail on", ["!set:Horde:Stats:LodDistance=40.0"], [], 4.0],
	]

# The arrived crowd with candidate changes, each between two baseline phases
# so heat drift shows up as the baselines moving rather than as a phantom
# saving. Pairs are [label, entry actions, restore actions].
static func _hordeab_timeline(count: int, pairs: Array) -> Array:
	var timeline := [
		["seed %d" % count, ["!set:Horde:Stats:HuntSeedMinRadius=30.0",
			"!set:Horde:Stats:HuntSeedRadius=150.0", "!set:Horde:Stats:AgentCount=%d" % count], [], 3.0],
		["reseed", ["!call:Horde:BenchReseed"], [], 12.0],
		["arriving", [], [], 12.0],
		["crowd on you", ["!set:Horde:Stats:HuntSeedMinRadius=150.0", "!set:Horde:Stats:HuntSeedRadius=450.0"], [], 4.0],
	]
	for pair in pairs:
		timeline.append(["base", [], [], 6.0])
		timeline.append([pair[0], pair[1], [], 6.0])
		timeline.append(["  restore", pair[2], [], 1.0])
	timeline.append(["base", [], [], 6.0])
	return timeline

const HORDE_AB_PAIRS := [
	["flow rebuild off", ["!set:Horde:Stats:FlowRebuildInterval=1000.0"], ["!set:Horde:Stats:FlowRebuildInterval=0.5"]],
	["horde one thread", ["!set:Horde:Stats:Multithreaded=false"], ["!set:Horde:Stats:Multithreaded=true"]],
	["ranged off", ["!set:Horde:Stats:RangedEnabled=false"], ["!set:Horde:Stats:RangedEnabled=true"]],
	["hud off", ["!hide:HudLayer"], ["!show:HudLayer"]],
]

# The machete's two strokes, caught mid-swing: left to right, then the
# chained return right to left, then a late click that has to start left to
# right again. Shots are taken a little into each strike, after the windup.
const MACHETE_TIMELINE := [
	["out", ["slot_2", "@status"], [], 1.0],
	["swing 1", ["fire"], [], 0.17],
	["swing 1 shot", ["#machete_1_l2r"], [], 0.45],
	["swing 2", ["fire"], [], 0.13],
	["swing 2 shot", ["#machete_2_r2l"], [], 1.4],
	["late swing", ["fire"], [], 0.17],
	["late swing shot", ["#machete_3_l2r"], [], 1.0],
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

var _status := false
var _pulse_timer := 0.0
var _status_next := 0.0

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
		"grapple": _timeline = GRAPPLE_TIMELINE
		"machete": _timeline = MACHETE_TIMELINE
		"mech": _timeline = MECH_TIMELINE
		"bike": _timeline = BIKE_TIMELINE
		"horde": _timeline = _horde_timeline(int(args[2]) if args.size() > 2 else 25000)
		"hordeab": _timeline = _hordeab_timeline(int(args[2]) if args.size() > 2 else 25000, HORDE_AB_PAIRS)
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
	if action == "@status":
		_status = not _status
		return
	if action.begins_with("@aim:"):
		var parts := action.substr(5).split(":")
		var target := root.find_child(parts[0], true, false) as Node3D
		var player := _player()
		if target == null or player == null:
			print("[bench] cannot ", action)
			return
		var height := float(parts[1]) if parts.size() > 1 else 0.0
		player.call("BenchLookAt", target.global_position + Vector3.UP * height)
		return
	if action.begins_with("!set:"):
		var parts := action.substr(5).split(":", true, 1)
		var pair := parts[1].split("=") if parts.size() == 2 else PackedStringArray()
		var node := root.find_child(parts[0], true, false)
		if node != null and pair.size() == 2:
			node.set_indexed(pair[0], str_to_var(pair[1]))
		else:
			print("[bench] cannot ", action)
		return
	if action.begins_with("!call:"):
		var call_parts := action.substr(6).split(":")
		var callee := root.find_child(call_parts[0], true, false)
		if callee != null and call_parts.size() == 2:
			callee.call(call_parts[1])
		else:
			print("[bench] cannot ", action)
		return
	_send(action, true)
	_send(action, false)

# The level's spawned player, through LevelRoot.Player.
func _player() -> Node:
	if current_scene == null or not ("Player" in current_scene):
		return null
	return current_scene.get("Player")

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
	if _status and _elapsed >= _status_next:
		_status_next = _elapsed + 0.5
		var label: String = _timeline[maxi(_phase, 0)][0]
		var player := _player()
		if player != null:
			print("[bench]   @ %-26s %s" % [label, player.call("BenchStatus")])
		var horde := root.find_child("Horde", true, false)
		if horde != null and horde.has_method("BenchStatus"):
			print("[bench]   @ %-26s %s" % [label, horde.call("BenchStatus")])

	_gpu_total += RenderingServer.viewport_get_measured_render_time_gpu(_vp)
	_worst = max(_worst, delta)
	_total += delta
	_frames += 1
	if delta > BUDGET:
		_over += 1

	_pulse_timer -= delta
	if _pulse_timer <= 0.0:
		_pulse_timer = PULSE_SECONDS
		for action in _held:
			if action.begins_with("~"):
				_tap(action.substr(1))

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
			if not next_held.has(action) and not action.begins_with("~"):
				_send(action, false)
		_phase = want
		_phase_started = _elapsed
		for action in entry[1]:
			_tap(action)
		for action in next_held:
			if not _held.has(action) and not action.begins_with("~"):
				_send(action, true)
		_held = next_held

	return false
