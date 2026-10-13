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
# weapon fired), both in lvl_horde_flats; planet [count] (stood on, lifted to
# orbit and back, then the crowd), in lvl_planet_test; drop (landed, a mesa,
# the arena from above, the nest) and extract (the pod home, boarded), in
# lvl_drop_test.
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
const SETTLE_TOLERANCE := 0.06 # fractional drift allowed in the draw count
const SETTLE_HOLD := 2.0 # seconds it has to stay within that
const SETTLE_MIN := 6.0 # never measure before this
const WARMUP_MAX := 40.0 # give up and measure anyway
const STEP := 4.0 # default phase length
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
#   "!call:Node:method"      call a method with no arguments; "Node/Child"
#                            for a child of that node
#   "@goto:x, y, z"          move the level's player there
#   "@aim:Node:height"       turn the player to look at that node, raised by
#                            height metres
#   "@status"                toggle a line of the player's and the horde's state
#                            twice a second
const IDLE_TIMELINE := [
	["idle", [], [], 6.0],
]

# lvl_drop_test (decision 0026): by the time the frames settle the pod has
# landed. The jar from the landing site, from a mesa and from high over the
# arena, then the nest walked up to until the horde comes.
const DROP_TIMELINE := [
	["landed, at the jar", ["@aim:Jar:20", "@status"], [], 4.0],
	["landed shot", ["#drop_landed"], [], 1.0],
	["west mesa", ["@goto:-230, 34, -40", "@aim:Jar:20"], [], 4.0],
	["west mesa shot", ["#drop_mesa"], [], 1.0],
	["over the arena", ["@goto:0, 220, 520", "@aim:Jar:0"], [], 1.0],
	["over the arena shot", ["#drop_overview"], [], 1.0],
	["at the nest", ["@goto:0, 2, 120", "@aim:Jar:20"], [], 8.0],
	["at the nest shot", ["#drop_nest"], [], 1.0],
]

# lvl_drop_test's ending: the job called done, the pod home falls to its
# site, the player is put in its hatch, and it climbs out to the end card.
const EXTRACT_TIMELINE := [
	["job done", ["!call:Mission:BenchJobDone", "@status"], [], 11.0],
	["pod down shot", ["#extract_pod"], [], 1.0],
	["boarding", ["!call:Mission:BenchToPod"], [], 9.0],
	["end card shot", ["#extract_end"], [], 1.0],
]

const LOOK_TIMELINE := [
	["look", ["#look"], [], 1.0],
	["look, no ink", ["!hide:ToonPass"], [], 0.5],
	["look, no ink shot", ["#look_noink"], [], 0.5],
	["ink back", ["!show:ToonPass"], [], 0.5],
]

# Where the GPU's time goes in the level's own view: each part switched off
# in turn, with a baseline phase between, so drift shows as drift.
const GPUAB_TIMELINE := [
	["base", [], [], 4.0],
	["no sun shadows", ["!set:Sun:shadow_enabled=false"], [], 4.0],
	["base again", ["!set:Sun:shadow_enabled=true"], [], 4.0],
	["horde not drawn", ["!hide:Horde"], [], 4.0],
	["base again 2", ["!show:Horde"], [], 4.0],
	["no downtown", ["!hide:Downtown"], [], 4.0],
	["base again 3", ["!show:Downtown"], [], 4.0],
	["no fog", ["!set:WorldEnvironment:environment:fog_enabled=false"], [], 4.0],
	["base again 4", ["!set:WorldEnvironment:environment:fog_enabled=true"], [], 4.0],
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
	["reel off the floor", [], ["grapple", "jump"], 0.6],
	["swing, building speed", [], ["grapple", "move_forward"], 2.0],
	["reel, keeping speed", [], ["grapple", "jump"], 0.9],
	["let go after speed reel", [], [], 0.4],
	["reset at spawn", ["@goto:0, 0.2, 70", "!set:Player:velocity=Vector3(0, 0, 0)", "@aim:Pillar6:24"], [], 0.6],
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
	["climb on, seat view", ["interact"], [], 1.0],
	["seat shot", ["#mech_seat", MECH_STATUS], [], 0.3],
	["forward", [], ["move_forward"], 2.0],
	["turret gun", ["@status"], ["fire"], 3.0],
	["turret shot", ["#mech_turret", MECH_STATUS], ["fire"], 0.3],
	["cannon", [], ["alt_fire"], 3.0],
	["cannon shot", ["#mech_cannon", MECH_STATUS], [], 0.3],
	["chase view", ["view_toggle", "@status"], [], 0.5],
	["strafe right", [], ["move_right"], 2.0],
	["strafe shot", ["#mech_strafe", MECH_STATUS], ["move_right"], 0.3],
	["camera swung, walking", ["!call:Companion:BenchTurnCamera", MECH_STATUS], ["move_forward"], 0.5],
	["turning shot", ["#mech_turn", MECH_STATUS], ["move_forward"], 0.3],
	["stop", [MECH_STATUS], [], 1.0],
	["jump", ["jump"], [], 0.45],
	["jets", [], ["jump"], 1.2],
	["jets shot", ["#mech_jets", MECH_STATUS], ["jump"], 0.2],
	["falling", [], [], 2.5],
	["climb off", ["interact"], [], 1.0],
	["guarding on its own", ["@aim:QuadrupedTank:2.5", MECH_STATUS, "@status"], [], 8.0],
	["guard shot", ["#mech_guard", MECH_STATUS, "@status"], [], 0.5],
]

# The three ally mechs side by side for scale, then the fighter walking,
# sprinting, ridden from its cockpit, firing with arms raised, slashing,
# and the storage bot keeping up.
const MECHS_TIMELINE := [
	["line-up", ["@aim:MobileStorage:0.5", "@status"], [], 0.6],
	["line-up shot", ["#mechs_lineup"], [], 0.3],
	["walk forward, they follow", [], ["move_forward"], 3.0],
	["turn round", ["@aim:FieldFighter:3"], [], 0.4],
	["following shot", ["#mechs_follow"], [], 0.3],
	["board the fighter", ["@aim:FieldFighter:3"], ["move_forward"], 2.5],
	["climb in", ["interact", "!call:Companion:PrintBenchStatus"], [], 1.0],
	["cockpit shot", ["#fighter_cockpit"], [], 0.3],
	["walk", [], ["move_forward"], 2.5],
	["sprint", [], ["move_forward", "sprint"], 2.0],
	["sprint shot", ["#fighter_sprint"], ["move_forward", "sprint"], 0.3],
	["fire", [], ["fire", "alt_fire"], 2.5],
	["firing shot", ["#fighter_fire"], ["fire"], 0.3],
	["slash selected", ["slot_3"], [], 0.2],
	["slash", [], ["fire"], 2.0],
	["slash shot", ["#fighter_slash"], ["fire"], 0.3],
	["gun again", ["slot_1"], [], 0.2],
	["chase view", ["view_toggle"], ["move_forward"], 1.5],
	["chase shot", ["#fighter_chase"], ["move_forward"], 0.3],
	["off", ["interact"], [], 1.0],
	["guarding", ["@aim:FieldFighter:3"], [], 6.0],
	["guard shot", ["#mechs_guard", "@status"], [], 0.3],
]

# What the vehicles cost to draw: every one shown, then hidden, in pairs,
# standing still facing them. Hidden is visibility only; they still think.
const MECHSAB_TIMELINE := [
	["face them", ["@aim:FieldFighter:3"], [], 3.0],
	["all shown", [], [], 5.0],
	["vehicles hidden", ["!hide:FieldFighter", "!hide:MobileStorage", "!hide:HoverBike", "!hide:QuadrupedTank"], [], 5.0],
	["all shown again", ["!show:FieldFighter", "!show:MobileStorage", "!show:HoverBike", "!show:QuadrupedTank"], [], 5.0],
	["vehicles hidden again", ["!hide:FieldFighter", "!hide:MobileStorage", "!hide:HoverBike", "!hide:QuadrupedTank"], [], 5.0],
	["all shown, last", ["!show:FieldFighter", "!show:MobileStorage", "!show:HoverBike", "!show:QuadrupedTank"], [], 5.0],
]

# The four weapons ported with the bike, into an arriving crowd: the strike
# designator (fires on the press, so tapped), the burner and the manipulator
# held, the lance tapped. Kills and the frame per weapon.
static func _arsenal_timeline() -> Array:
	return [
		["seed", ["@status", "!set:Horde:Stats:HuntSeedMinRadius=30.0",
			"!set:Horde:Stats:HuntSeedRadius=120.0"], [], 2.0],
		["reseed", ["!call:Horde:BenchReseed"], [], 10.0],
		["face the crowd", ["@aim:Horde:0.5"], [], 2.0],
		["gravity manipulator", ["slot_8", "@aim:Horde:0.5"], ["alt_fire"], 4.0],
		["manipulator shot", ["#arsenal_manipulator"], ["alt_fire"], 0.3],
		["let go", [], [], 2.0],
		["manipulator, fire", [], ["fire"], 3.0],
		["strike designator", ["slot_4"], ["~fire"], 9.0],
		["strike shot", ["#arsenal_strike"], [], 0.3],
		["brine burner", ["slot_6", "@aim:Horde:0.5"], ["fire"], 5.0],
		["burner shot", ["#arsenal_burner"], ["fire"], 0.3],
		["vinegar lance", ["slot_7", "@aim:Horde:0.5"], ["~fire"], 5.0],
		["lance shot", ["#arsenal_lance"], ["~fire"], 0.3],
		["hold off for the tear", ["!set:Horde:Stats:HuntSeedMinRadius=400.0", "!call:Horde:BenchReseed",
			"@goto:-60, 0.2, -40"], [], 2.0],
		["aim the facade", ["slot_8", "@aim:Tower11:20"], [], 1.0],
		["tug a chunk", ["@aim:Tower11:20"], ["fire"], 2.5],
		["the rip", ["#arsenal_tear"], ["fire"], 0.3],
		["throw it into the tower", [], [], 2.0],
		["thrown shot", ["#arsenal_thrown", "@aim:Tower11:20"], [], 0.3],
	]

# Voxel destruction against the compound: a pillar shot at its base with the
# rifle, then rockets, then a strike, with the debris count and shots. The
# horde is held far away so the frame is the destruction's.
const WRECK_STATUS := "!call:VoxelDebris:PrintBenchStatus"
const WRECK_TIMELINE := [
	["hold the horde off", ["@status", "!set:Horde:Stats:HuntSeedMinRadius=400.0", "!call:Horde:BenchReseed",
		"@goto:-24, 0.2, 4"], [], 2.0],
	["aim at the pillar base", ["@aim:Pillar1:3", "@status"], [], 1.0],
	["before", ["#wreck_before", WRECK_STATUS], [], 0.3],
	["rifle at the base", ["slot_1", "@aim:Pillar1:3"], ["fire"], 3.0],
	["rifle shot", ["#wreck_rifle", WRECK_STATUS], [], 0.3],
	["rockets at the base", ["slot_3", "@aim:Pillar1:3"], ["~fire"], 3.0],
	["falling", ["@aim:Pillar1:12", WRECK_STATUS], [], 1.0],
	["falling shot", ["#wreck_falling"], [], 0.3],
	["rockets shot", ["@aim:Pillar1:3"], [], 1.5],
	["landed shot", ["#wreck_landed", WRECK_STATUS], [], 0.3],
	["settling", [], [], 3.0],
	["settled shot", ["#wreck_settled", WRECK_STATUS], [], 0.3],
	["strike on a block", ["slot_4", "@aim:Column1:0"], ["~fire"], 6.0],
	["strike shot", ["#wreck_strike", WRECK_STATUS], [], 0.3],
	["after", [], [], 4.0],
	["after shot", ["#wreck_after", WRECK_STATUS], [], 0.3],
]

# The allies broken by the same rules as everything else: the tank's legs shot
# out from under it, rockets into its body until it falls apart, then the
# fighter. The horde is held off so the allies stand still to be shot.
const MECHWRECK_TIMELINE := [
	["hold the horde off", ["!set:Horde:Stats:HuntSeedMinRadius=400.0", "!call:Horde:BenchReseed"], [], 3.0],
	["aim at the tank's legs", ["@aim:QuadrupedTank:0.6"], [], 0.5],
	["before", ["#mechwreck_before"], [], 0.3],
	["rifle at the legs", ["slot_1", "@aim:QuadrupedTank:0.6"], ["fire"], 4.0],
	["legs shot", ["#mechwreck_legs", WRECK_STATUS], [], 0.3],
	["rockets at the body", ["slot_3", "@aim:QuadrupedTank:3.5"], ["~fire"], 4.0],
	["body shot", ["#mechwreck_body", WRECK_STATUS], [], 0.3],
	["more rockets", ["@aim:QuadrupedTank:3.5"], ["~fire"], 4.0],
	["falling apart", [WRECK_STATUS], [], 2.0],
	["tank shot", ["#mechwreck_tank", WRECK_STATUS], [], 0.3],
	["rockets at the fighter", ["@aim:FieldFighter:4"], ["~fire"], 5.0],
	["fighter shot", ["#mechwreck_fighter", WRECK_STATUS], [], 0.3],
	["after", [], [], 3.0],
	["after shot", ["#mechwreck_after", WRECK_STATUS], [], 0.3],
]

# A skyscraper brought down (docs/decisions/0013): first what standing in the
# downtown block costs, then rockets and a strike into one tower's lobby until
# it can no longer carry itself, the fall, and the rubble settling. The horde
# is held off so the frame is the collapse's.
const COLLAPSE_TIMELINE := [
	["hold the horde off", ["@status", "!set:Horde:Stats:HuntSeedMinRadius=400.0", "!call:Horde:BenchReseed",
		"@goto:-60, 0.2, -96"], [], 2.0],
	["downtown", ["@aim:Tower11:45", "@status"], [], 3.0],
	["downtown shot", ["#collapse_downtown", WRECK_STATUS], [], 0.3],
	["rockets at the lobby", ["slot_3", "@aim:Tower11:2"], ["~fire"], 6.0],
	["rockets shot", ["#collapse_rockets", WRECK_STATUS], [], 0.3],
	["strike on the lobby", ["slot_4", "@aim:Tower11:0"], ["~fire"], 6.0],
	["strike shot", ["@aim:Tower11:25", "#collapse_strike", WRECK_STATUS], [], 0.3],
	["coming down", ["@aim:Tower11:25"], [], 2.0],
	["coming down shot", ["#collapse_falling", WRECK_STATUS], [], 0.3],
	["landing", [], [], 3.0],
	["landed shot", ["@aim:Tower11:5", "#collapse_landed", WRECK_STATUS], [], 0.3],
	["settling", [], [], 5.0],
	["settled shot", ["#collapse_settled", WRECK_STATUS], [], 0.3],
]

# The bike's real beam swept through Tower11, not the instant slit of cut.
# Quiet on purpose: graph-upkeep events are what the gate reads.
static func _lasercut_timeline() -> Array:
	return [
		["hold the horde off", ["!set:Horde:Stats:HuntSeedMinRadius=400.0", "!call:Horde:BenchReseed",
			"@goto:-60, 0.2, -40"], [], 2.0],
		["board", ["!call:Systems:BoardNow"], [], 1.0],
		["look", ["@aim:Tower11:20", "!call:PerfRecorder:Start", "!silent"], [], 1.0],
		["beam", ["slot_2", "@aim:Tower11:12"], ["fire"], 6.0],
		["falling", ["@aim:Tower11:30"], [], 8.0],
		["stop", ["!call:PerfRecorder:Stop", "!loud", "!call:VoxelDebris:PrintBenchStatus"], [], 0.3],
	]

# A tower cut by a slit 2 m up, as a beam would leave it: clean through
# (cut), it drops whole and crushes down storey by storey; through 60% of
# one side (cutside), it hangs on what is left, leans over and tears free.
# Watched from 85 m off with the biggest falling piece printed every phase.
# With "quiet" there are no screenshots and no status lines, and every frame
# from before the cut to the end is recorded to perflogs/ (PerfRecorder): the
# run to read worst frames and the cause of each from.
static func _cut_timeline(method: String, shot: String, quiet: bool, seconds := 13.0) -> Array:
	var status := "!call:VoxelDebris:PrintBenchStatus"
	var timeline := [
		["hold the horde off", ([] if quiet else ["@status"]) + ["!set:Horde:Stats:HuntSeedMinRadius=400.0",
			"!call:Horde:BenchReseed", "@goto:-60, 0.2, -40"], [], 2.0],
		["look", ["@aim:Tower11:40"] + (["!call:PerfRecorder:Start", "!silent"] if quiet else []), [], 2.0],
		["cut", ["!call:Tower11/Voxels:%s" % method], [], 0.5],
	]
	if quiet:
		# One silent phase: a console line costs the frame it is printed in
		# 15-40 ms on Windows, which is the very hitch being looked for.
		timeline.append(["falling", [], [], seconds])
		timeline.append(["stop", ["!call:PerfRecorder:Stop", "!loud", status], [], 0.3])
		return timeline
	for i in 16:
		timeline.append(["t+%.1f" % (0.5 + i * 0.5), [status], [], 0.5])
		if i % 2 == 1:
			timeline.append(["%s %d" % [shot, i], ["#%s_%02d" % [shot, i]], [], 0.3])
	timeline.append(["settling", [status], [], 5.0])
	timeline.append(["settled shot", ["#%s_settled" % shot, status], [], 0.3])
	# The rubble is still voxels: rockets into the heap.
	timeline.append(["rockets at the rubble", ["slot_3", "@aim:Tower11:0", status], ["~fire"], 4.0])
	timeline.append(["dust clears", [], [], 2.0])
	timeline.append(["rubble shot", ["#%s_rubble_shot" % shot, status], [], 0.3])
	# And it is still loose: the gravity gun tears a lump off the heap,
	# holds it, and throws it.
	timeline.append(["walk up to the rubble", ["@goto:-60, 0.2, -98", "@aim:Tower11:0", "slot_8"], [], 1.5])
	timeline.append(["grab the rubble", ["@aim:Tower11:0"], ["fire"], 1.2])
	timeline.append(["holding shot", ["#%s_grab" % shot, status], ["fire"], 0.3])
	timeline.append(["throw", [], [], 1.2])
	timeline.append(["thrown shot", ["#%s_thrown" % shot, status], [], 0.3])
	return timeline

# A tall tree (voxel_destruction_tree.tres): rockets into the canopy, where
# only what is shot free should fall, then a slit 2 m up the trunk, and the
# whole tree comes down and snaps where it lands. Watched from about 60 m.
# "quiet" records every frame from the first rocket and prints nothing.
static func _tree_timeline(quiet: bool) -> Array:
	var status := "!call:VoxelDebris:PrintBenchStatus"
	var tree := "prp_tree_tall_a2"
	var timeline := [
		["hold the horde off", ([] if quiet else ["@status"]) + ["!set:Horde:Stats:HuntSeedMinRadius=400.0",
			"!call:Horde:BenchReseed", "@goto:-40, 0.2, 120"], [], 2.0],
		["look", ["@aim:%s:40" % tree, "slot_3"] + (["!call:PerfRecorder:Start", "!silent"] if quiet else []), [], 2.0],
		["rockets at the canopy", ["@aim:%s:48" % tree], ["~fire"], 4.0],
		["branches falling", [] if quiet else [status], [], 4.0],
	]
	if not quiet:
		timeline.append(["canopy shot", ["#tree_canopy", status], [], 0.3])
	timeline.append(["cut", ["@aim:%s:30" % tree, "!call:%s/Voxels:BenchCutThrough" % tree], [], 0.5])
	if quiet:
		timeline.append(["falling", [], [], 12.0])
		timeline.append(["stop", ["!call:PerfRecorder:Stop", "!loud", status], [], 0.3])
		return timeline
	for i in 8:
		timeline.append(["t+%.1f" % (0.5 + i * 1.0), [status], [], 1.0])
		if i % 2 == 1:
			timeline.append(["tree %d" % i, ["#tree_%02d" % i], [], 0.3])
	timeline.append(["settling", [status], [], 4.0])
	timeline.append(["settled shot", ["#tree_settled", status], [], 0.3])
	return timeline

# Airstrikes called on a tower as fast as the designator will take them, every
# frame recorded and nothing printed meanwhile: the hitches of a strike spam.
const STRIKE_TIMELINE := [
	["hold the horde off", ["!set:Horde:Stats:HuntSeedMinRadius=400.0", "!call:Horde:BenchReseed",
		"@goto:-60, 0.2, -60"], [], 2.0],
	["aim", ["@aim:Tower11:20", "slot_4", "!call:PerfRecorder:Start", "!silent"], [], 2.0],
	["strikes", ["@aim:Tower11:20"], ["~fire"], 10.0],
	["after", ["@aim:Tower11:20"], [], 6.0],
	["stop", ["!call:PerfRecorder:Stop", "!loud", "!call:VoxelDebris:PrintBenchStatus"], [], 0.3],
]

# The crowd brought onto the player and the three allies: the allies guard,
# fire and stomp, the pickles bite, strike and spit at them. Every ally's body
# and legs printed as it goes; the frame is a full fight.
static func _siege_timeline(count: int) -> Array:
	return [
		["seed %d close" % count, ["@status", "!set:Horde:Stats:HuntSeedMinRadius=40.0",
			"!set:Horde:Stats:HuntSeedRadius=140.0", "!set:Horde:Stats:AgentCount=%d" % count, MECH_STATUS], [], 3.0],
		["reseed", ["!call:Horde:BenchReseed"], [], 8.0],
		["arriving", [MECH_STATUS], [], 8.0],
		["fighting", [MECH_STATUS, "@aim:QuadrupedTank:2"], [], 8.0],
		["fight shot", ["#siege_fight"], [], 0.3],
		["still fighting", [MECH_STATUS, "!set:Horde:Stats:HuntSeedMinRadius=150.0", "!set:Horde:Stats:HuntSeedRadius=450.0"], [], 10.0],
		["late", [MECH_STATUS, "@aim:QuadrupedTank:2"], [], 8.0],
		["late shot", ["#siege_late", MECH_STATUS], [], 0.3],
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

# The planet (lvl_planet_test): stood on it, lifted off it in three steps to
# where the whole planet is in view (the far shell and the near chunks' draw
# distance), back down, and then the crowd arriving over its ground. Positions
# assume the test level's site at the top of a 1200 m planet; the player is
# aimed back at the site each time so the planet is what fills the view.
static func _planet_timeline(count: int) -> Array:
	return [
		["surface", [], [], 6.0],
		["surface shot", ["#planet_surface"], [], 0.5],
	] + _planet_heights("planet") + [
		["back down", ["@lift:25:70"], [], 6.0],
		["seed %d" % count, ["!set:Horde:Stats:HuntSeedMinRadius=30.0",
			"!set:Horde:Stats:HuntSeedRadius=150.0", "!set:Horde:Stats:AgentCount=%d" % count], [], 3.0],
		["reseed", ["!call:Horde:BenchReseed"], [], 12.0],
		["arriving", [], [], 12.0],
		["crowd on you", ["!set:Horde:Stats:HuntSeedMinRadius=150.0", "!set:Horde:Stats:HuntSeedRadius=450.0"], [], 8.0],
		["crowd shot", ["@aim:Horde:-40", "#planet_crowd"], [], 0.5],
		["crowd, rockets", ["slot_3", "@aim:Horde:-20"], ["~fire"], 5.0],
		["after", [], [], 5.0],
	]

# Lifted off the planet in three steps to where the whole of it is in view,
# aimed back at the site each time. Heights are over the real ground under the
# site, so they hold on any radius.
static func _planet_heights(shot: String) -> Array:
	return [
		["300 m up", ["@lift:300:120", "@aim:Site:0"], [], 3.0],
		["300 m shot", ["#%s_300m" % shot], [], 0.5],
		["1 km up", ["@lift:1000:400", "@aim:Site:0"], [], 3.0],
		["1 km shot", ["#%s_1km" % shot], [], 0.5],
		["orbit 2.4 km", ["@lift:2400:600", "@aim:Site:0"], [], 4.0],
		["orbit shot", ["#%s_orbit" % shot], [], 0.5],
	]

# A sweep of n seeds through lvl_planet_test: each one rebuilt, stood on,
# lifted to orbit. Every planet a contract can roll must hold the budget, so
# this is what the archetype's ranges are widened against. The planet prints
# each seed's shape, biome and build time as it builds.
static func _planets_timeline(count: int, first: int) -> Array:
	var timeline := []
	for i in count:
		var planet_seed := first + i
		timeline.append_array([
			["seed %d build" % planet_seed, ["!reload:%d" % planet_seed], [], 7.0],
			["seed %d surface" % planet_seed, ["@lift:2:70", "@aim:Site:10", "#seed%d_surface" % planet_seed], [], 4.0],
		])
		for phase in _planet_heights("seed%d" % planet_seed):
			timeline.append(["seed %d %s" % [planet_seed, phase[0]], phase[1], phase[2], phase[3]])
	return timeline

# The arrival in lvl_contract: on the bike beside the ship in orbit, then
# cruising flat out at the compound, nose already on it, into the air and down
# to the ground. What it measures is the chunks streaming in under a fast
# descent: read the worst frames from the descent phases.
const DESCENT_TIMELINE := [
	["at the ship", [], [], 4.0],
	["ship shot", ["#arrival_ship"], [], 0.5],
	["cruise", ["ship_mode_toggle"], ["move_forward", "sprint"], 6.0],
	["descent shot 1", ["#arrival_descent_1"], ["move_forward", "sprint"], 0.5],
	["descent", [], ["move_forward", "sprint"], 5.0],
	["descent shot 2", ["#arrival_descent_2"], ["move_forward", "sprint"], 0.5],
	["descent, late", [], ["move_forward"], 5.0],
	["descent shot 3", ["#arrival_descent_3"], ["move_forward"], 0.5],
	["coast", [], ["move_forward"], 4.0],
	["hover", ["ship_mode_toggle"], [], 4.0],
	["hover shot", ["#arrival_hover"], [], 0.5],
	["status", ["@status"], [], 1.0],
]

# The horde at home: the player waits in orbit, past the leash, and how the
# garrison stands round its compound is printed as it settles. With a roam
# radius it spreads over the compound; without, it piles into one cell.
const GARRISON_SPREAD := "!call:Spawns/Horde:PrintBenchSpread"
const GARRISON_TIMELINE := [
	["arrive", [GARRISON_SPREAD], [], 5.0],
	["10 s", [GARRISON_SPREAD], [], 10.0],
	["20 s", [GARRISON_SPREAD], [], 10.0],
	["30 s", [GARRISON_SPREAD], [], 10.0],
	["40 s", [GARRISON_SPREAD], [], 10.0],
	["end", [GARRISON_SPREAD], [], 0.5],
]

# The whole way in, from lvl_ship_hub: the ship, its computer, the first
# contract taken, the contract level loading behind its screen, and the
# arrival in orbit. The scene changes under the bench mid-run.
const FLOW_TIMELINE := [
	["in the ship", [], [], 3.0],
	["ship shot", ["#flow_ship"], [], 0.5],
	["the computer", ["!call:Terminal:BenchOpen"], [], 1.0],
	["board shot", ["#flow_board"], [], 0.5],
	["take the first", ["!call:Terminal:BenchTake"], [], 1.0],
	["loading", [], [], 9.0],
	["in orbit", [], [], 4.0],
	["orbit shot", ["#flow_orbit"], [], 0.5],
]

# What the planet costs the GPU, on the ground and 300 m up: the whole planet
# hidden, the far shell hidden, and the terrain shader's grid and hatching off,
# each between baselines.
static func _planetab_timeline() -> Array:
	var timeline := []
	for spot in [["ground", "25:70", "Horde:20"], ["300 m", "300:120", "Site:0"]]:
		timeline.append_array([
			["%s base" % spot[0], ["@lift:" + spot[1], "!set:Player:velocity=Vector3(0, 0, 0)", "@aim:" + spot[2]], [], 4.0],
			["%s no planet" % spot[0], ["@lift:" + spot[1], "!set:Player:velocity=Vector3(0, 0, 0)", "!hide:Planet"], [], 4.0],
			["%s base 2" % spot[0], ["@lift:" + spot[1], "!set:Player:velocity=Vector3(0, 0, 0)", "!show:Planet"], [], 4.0],
			["%s no far shell" % spot[0], ["@lift:" + spot[1], "!set:Player:velocity=Vector3(0, 0, 0)", "!hide:FarShell"], [], 4.0],
			["%s base 3" % spot[0], ["@lift:" + spot[1], "!set:Player:velocity=Vector3(0, 0, 0)", "!show:FarShell"], [], 4.0],
			["%s plain shader" % spot[0], ["@lift:" + spot[1],
				"!set:Planet:TerrainMaterial:shader_parameter/grid_strength=0.0",
				"!set:Planet:TerrainMaterial:shader_parameter/hatch_strength=0.0"], [], 4.0],
			["%s base 4" % spot[0], ["@lift:" + spot[1],
				"!set:Planet:TerrainMaterial:shader_parameter/grid_strength=0.7",
				"!set:Planet:TerrainMaterial:shader_parameter/hatch_strength=0.85"], [], 4.0],
			["%s no buildings" % spot[0], ["@lift:" + spot[1], "!set:Player:velocity=Vector3(0, 0, 0)", "!hide:Site"], [], 4.0],
			["%s base 5" % spot[0], ["@lift:" + spot[1], "!set:Player:velocity=Vector3(0, 0, 0)", "!show:Site"], [], 4.0],
		])
	return timeline

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
var _path := ""
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
	# The main scene is the title menu, which measures nothing: with no level
	# named, the contract level is benched, on its fallback seed.
	var path: String = args[0] if args.size() > 0 else "res://scenes/levels/lvl_contract.tscn"
	var mode: String = args[1] if args.size() > 1 else "idle"

	match mode:
		"look": _timeline = LOOK_TIMELINE
		"drop": _timeline = DROP_TIMELINE
		"extract": _timeline = EXTRACT_TIMELINE
		"ink": _timeline = INK_TIMELINE
		"gpuab": _timeline = GPUAB_TIMELINE
		"strike": _timeline = STRIKE_TIMELINE
		"grapple": _timeline = GRAPPLE_TIMELINE
		"machete": _timeline = MACHETE_TIMELINE
		"mech": _timeline = MECH_TIMELINE
		"bike": _timeline = BIKE_TIMELINE
		"mechs": _timeline = MECHS_TIMELINE
		"mechsab": _timeline = MECHSAB_TIMELINE
		"arsenal": _timeline = _arsenal_timeline()
		"wreck": _timeline = WRECK_TIMELINE
		"mechwreck": _timeline = MECHWRECK_TIMELINE
		"collapse": _timeline = COLLAPSE_TIMELINE
		"lasercut": _timeline = _lasercut_timeline()
		"cut": _timeline = _cut_timeline("BenchCutThrough", "cut", args.size() > 2 and args[2] == "quiet",
			float(args[3]) if args.size() > 3 else 13.0)
		"cutside": _timeline = _cut_timeline("BenchCutSide", "cutside", args.size() > 2 and args[2] == "quiet",
			float(args[3]) if args.size() > 3 else 13.0)
		"tree": _timeline = _tree_timeline(args.size() > 2 and args[2] == "quiet")
		"siege": _timeline = _siege_timeline(int(args[2]) if args.size() > 2 else 25000)
		"horde": _timeline = _horde_timeline(int(args[2]) if args.size() > 2 else 25000)
		"hordeab": _timeline = _hordeab_timeline(int(args[2]) if args.size() > 2 else 25000, HORDE_AB_PAIRS)
		"planet": _timeline = _planet_timeline(int(args[2]) if args.size() > 2 else 25000)
		"planetab": _timeline = _planetab_timeline()
		"descent": _timeline = DESCENT_TIMELINE
		"garrison": _timeline = GARRISON_TIMELINE
		"flow": _timeline = FLOW_TIMELINE
		"planets": _timeline = _planets_timeline(int(args[2]) if args.size() > 2 else 10, int(args[3]) if args.size() > 3 else 1)
		_: _timeline = IDLE_TIMELINE

	print("=== bench: ", path, "  mode=", mode, "  window ", DisplayServer.window_get_size(),
		"  vsync=", DisplayServer.window_get_vsync_mode(), " (0=disabled) ===")

	_path = path
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
	if action.begins_with("!reload:"):
		# The level rebuilt from scratch with another planet seed.
		root.set_meta("bench_planet_seed", int(action.substr(8)))
		# Queued, not freed: freed here, mid-frame, the level's bodies could
		# still be referenced. The two overlap for a frame, which the planet
		# allows for (WorldUp counts planets).
		if current_scene != null:
			current_scene.queue_free()
		_found.clear()
		var level: Node = load(_path).instantiate()
		root.add_child(level)
		current_scene = level
		return
	if action.begins_with("@lift:"):
		# Over the real ground under the site: altitude, then metres back.
		var lift := action.substr(6).split(":")
		var planet := _find("Planet")
		var site := _find("Site") as Node3D
		var lifted := _player() as Node3D
		if planet == null or site == null or lifted == null:
			print("[bench] cannot ", action)
			return
		var up := site.global_position.normalized()
		var back := float(lift[1]) if lift.size() > 1 else 0.0
		lifted.global_position = planet.call("PointAbove", up, float(lift[0])) + site.global_basis.z * back
		if "velocity" in lifted:
			lifted.velocity = Vector3.ZERO
		return
	if action.begins_with("@goto:"):
		var player := _player() as Node3D
		var at = str_to_var("Vector3(%s)" % action.substr(6))
		if player != null and at is Vector3:
			player.global_position = at
		else:
			print("[bench] cannot ", action)
		return
	if action.begins_with("@aim:"):
		var parts := action.substr(5).split(":")
		var target := _find(parts[0]) as Node3D
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
		var node := _find(parts[0])
		if node != null and pair.size() == 2:
			node.set_indexed(pair[0], str_to_var(pair[1]))
		else:
			print("[bench] cannot ", action)
		return
	if action == "!silent":
		_silent = true
		return
	if action == "!loud":
		_silent = false
		return
	if action.begins_with("!call:"):
		var call_parts := action.substr(6).split(":")
		# "Node/Child" names a child of the first node of that name.
		var path := call_parts[0].split("/", true, 1)
		var callee := _find(path[0])
		if callee != null and path.size() == 2:
			callee = callee.get_node_or_null(path[1])
		if callee != null and call_parts.size() == 2:
			callee.call(call_parts[1])
		else:
			print("[bench] cannot ", action)
		return
	_send(action, true)
	_send(action, false)

# A node by name, searched for once. A search walks the whole tree, and with a
# level, a horde and a collapse's debris in it that took 30-45 ms: done at
# every phase boundary, it showed up as a hitch in the very run measuring one.
var _found := {}

func _find(node_name: String) -> Node:
	# Untyped first: a cached node freed by a scene change cannot be assigned
	# to a typed variable at all.
	var cached = _found.get(node_name)
	var node: Node = cached if is_instance_valid(cached) else null
	if node == null or not node.is_inside_tree():
		node = root.find_child(node_name, true, false)
		_found[node_name] = node
	return node

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

# "!silent" holds back the phase reports until "!loud": printing is not free.
var _silent := false

func _report(label: String) -> void:
	if _silent:
		return
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
	var driver := _find("PerfDriver")
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
		var horde := _find("Horde")
		if horde != null and horde.has_method("BenchStatus"):
			print("[bench]   @ %-26s %s" % [label, horde.call("BenchStatus")])
		for other in ["Planet", "SolarSystem"]:
			var node := _find(other)
			if node != null and node.has_method("BenchStatus"):
				print("[bench]   @ %-26s %s" % [label, node.call("BenchStatus")])

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
