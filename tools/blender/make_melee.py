"""The Field Fighter's wide hooks and its leaping slam, as Blender actions on
the rig in art/_source/FieldFighter/fighter_moves.blend, exported through the
add-on as resources/vehicles/moves/mch_field_fighter_<action>.tres:

    swing_l   the left fist cocked out and behind, swept through the front to
              the right in one wide, hard arc (three 'hit' marks along it)
    swing_r   the same, mirrored
    leap_slam both fists: a crouch, a leap, the fists thrown overhead and
              brought down onto the ground

    blender --background --factory-startup --python tools/blender/make_melee.py

Opens nothing of the user's: it loads the moves file itself, adds the actions
(replacing any of the same name), exports them and saves it.

A swing keys only its striking arm. The walker adds the hips' turn, the
lunge and the back foot's step from the move's Body lines (set below), so the
swing works on whatever legs the mech has left. The leap keys every part, as
the whole machine leaves the ground; the walker adds nothing to it
(BodyAuto off). The game plays a move only while every part it claims is on
the mech: a lost arm or leg withholds it, or stops it mid-way.

Keys are baked on every frame from the key poses at the top of each move, as
make_slam.py did. To change a move, edit its table and run this again; the
Lab tab's timing and the Body lines it saved are kept by Export.
"""
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
ROOT = os.path.dirname(os.path.dirname(HERE))
BLEND = os.path.join(ROOT, 'art', '_source', 'FieldFighter', 'fighter_moves.blend')

import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402
import dill_mech  # noqa: E402
from dill_mech import export  # noqa: E402

FPS = 60
GROUND_CLEAR = 0.12      # a fist stops this far above the ground, m
HIP = Vector((0.0, 0.0, 4.895))  # the legs' hips: what the body leans about


# ---------------------------------------------------------------- key poses

def K(frame, ease='smooth', **values):
    return frame, ease, values


# Each key is (frame, how it arrives, what changes); what it leaves out stays
# as it was. yaw: degrees the arm is swung about the vertical, positive
# cocking it behind its own side; phi: degrees from hanging (90 is level);
# twist: roll about the arm's own length.
SWING = (
    K(0, yaw=0, phi=0, twist=0),
    K(7, 'smooth', yaw=-8, phi=18, twist=10),         # a breath the other way
    K(20, 'out', yaw=64, phi=108, twist=-28),         # cocked out and back
    K(23, 'lin', yaw=68, phi=112, twist=-30),         # held a moment
    K(31, 'in', yaw=-34, phi=94, twist=0),            # the arc, fastest in front
    K(36, 'out', yaw=-122, phi=86, twist=14),
    K(42, 'out', yaw=-134, phi=76, twist=24),         # follows through
    K(54, 'smooth', yaw=-100, phi=44, twist=10),
    K(66, 'smooth', yaw=0, phi=0, twist=0),
)
SWING_HITS = (12.0, -40.0, -92.0)  # where on the arc the blows land, yaw degrees

# rise: how far the hips are above standing (m); lean: the body forward,
# degrees; th: each fist's pitch from hanging, degrees (negative is forward);
# spread: how far the arms stand out; tuck: how much the feet are drawn up
# toward the hips (m); ground: 0-1, th replaced by the pitch that puts the
# fists just on the ground.
LEAP = (
    K(0, rise=0, lean=0, th=0, spread=0, tuck=0, ground=0),
    K(13, 'smooth', rise=-1.05, lean=14, th=52, spread=10),     # gathers, arms back
    K(17, 'out', rise=-1.2, lean=17, th=58, spread=12),
    K(23, 'in', rise=0.6, lean=2, th=0, spread=14),             # drives up
    K(31, 'out', rise=2.8, lean=-8, th=-165, spread=20, tuck=1.2),
    K(34, 'lin', rise=2.9, lean=-10, th=-172, spread=20, tuck=1.4),
    K(40, 'in', rise=0.4, lean=14, th=-112, spread=14, tuck=0.2),  # falls
    K(44, 'in', rise=-1.9, lean=22, th=-40, spread=10, tuck=0, ground=1),
    K(47, 'out', rise=-2.1, lean=26, th=-40, ground=1),         # lands, squashes
    K(53, 'smooth', rise=-1.95, lean=22, th=-40, ground=1),
    K(60, 'smooth', rise=-1.4, lean=12, th=-30, spread=8, ground=0.5),
    K(80, 'smooth', rise=0, lean=0, th=0, spread=0, ground=0),
)
LEAP_IMPACT = 44

# The walker's body numbers for each (MechMove Body lines): the hips turn the
# striking side through the blow, drop and drive forward, the back foot steps.
SWING_BODY = ('BodyAuto = false', 'BodyLunge = 0.55', 'BodyDrop = 0.12', 'BodyLeanDegrees = 8',
              'BodyTwistDegrees = 46', 'BodyStep = 0.8', 'BodyWindup = 0.5')
LEAP_BODY = ('BodyAuto = false',)

EASE = {
    'lin': lambda t: t,
    'smooth': lambda t: t * t * (3.0 - 2.0 * t),
    'in': lambda t: t * t,
    'out': lambda t: 1.0 - (1.0 - t) ** 2,
}


def track(keys):
    """{name: value} for every frame from the first key to the last."""
    filled = []
    state = {}
    for frame, ease, values in keys:
        state = dict(state, **values)
        filled.append((frame, ease, state))
    out = []
    for (f0, _, a), (f1, ease, b) in zip(filled, filled[1:]):
        for f in range(f0, f1):
            t = EASE[ease]((f - f0) / (f1 - f0))
            out.append({k: a[k] + (b[k] - a[k]) * t for k in a})
    out.append(dict(filled[-1][2]))
    return out


# ---------------------------------------------------------------- the rig

def T(v):
    return Matrix.Translation(v)


def about(m, pivot):
    return T(pivot) @ m @ T(-pivot)


def rot(axis, deg):
    return Matrix.Rotation(math.radians(deg), 4, axis)


class Rig:
    def __init__(self):
        self.rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
        assert self.rig.matrix_world.is_identity or max(abs(a - b) for ra, rb in zip(self.rig.matrix_world, Matrix.Identity(4)) for a, b in zip(ra, rb)) < 1e-5
        self.pose = self.rig.pose.bones
        bones = self.rig.data.bones
        self.rest = {b.name: b.matrix_local.copy() for b in bones}
        self.rest3 = {n: m.to_3x3().to_4x4() for n, m in self.rest.items()}
        self.head = {b.name: b.head_local.copy() for b in bones}
        self.axis = {b.name: (b.tail_local - b.head_local).normalized() for b in bones}
        self.length = {b.name: (b.tail_local - b.head_local).length for b in bones}
        self.parent = {b.name: b.parent.name if b.parent else None for b in bones}
        self.rig.animation_data_create()
        self.rig.animation_data.action = None
        for pb in self.pose:
            pb.matrix_basis = Matrix.Identity(4)
        bpy.context.view_layer.update()
        # Every part's corners at rest, in the rig's space.
        self.corners = {}
        for o in export.part_objects(self.rig):
            self.corners[o['dill_part']] = [o.matrix_world @ Vector(c) for c in o.bound_box]

    def delta(self, name, m):
        """A bone's desired armature-space matrix as a rigid change to its
        rest: where each of its part's rest points goes."""
        return m @ self.rest[name].inverted()

    def low(self, name, m):
        return min((self.delta(name, m) @ c).z for c in self.corners[name])

    def apply(self, desired):
        """Poses the bones at once, parents first, from {name: matrix}."""
        order = sorted(desired, key=lambda n: self._depth(n))
        for n in order:
            p = self.parent[n]
            if p is None:
                local = self.rest[n]
                parent_pose = Matrix.Identity(4)
            else:
                parent_pose = desired.get(p, self.rest[p])
                local = self.rest[p].inverted() @ self.rest[n]
            basis = (parent_pose @ local).inverted() @ desired[n]
            self.pose[n].matrix_basis = basis

    def _depth(self, n):
        d = 0
        while self.parent[n]:
            n = self.parent[n]
            d += 1
        return d

    def key(self, names, frame):
        for n in names:
            pb = self.pose[n]
            pb.rotation_mode = 'QUATERNION'
            loc, quat, _ = pb.matrix_basis.decompose()
            pb.location = loc
            pb.rotation_quaternion = quat
            pb.keyframe_insert('rotation_quaternion', frame=frame)
            pb.keyframe_insert('location', frame=frame)

    def legs_free(self, on):
        """The feet's IK and their copied turn, off while a move poses the
        legs by hand, on otherwise. Keyed, so the action carries it."""
        for pb in self.pose:
            for c in pb.constraints:
                if c.type in {'IK', 'COPY_ROTATION'}:
                    c.influence = 0.0 if on else 1.0
                    c.keyframe_insert('influence', frame=0)

    def new_action(self, name):
        old = bpy.data.actions.get(name)
        if old is not None:
            bpy.data.actions.remove(old)
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        self.rig.animation_data.action = action
        for pb in self.pose:
            pb.matrix_basis = Matrix.Identity(4)
        return action


# ---------------------------------------------------------------- the swings

def arm_pose(r, arm, side, yaw, phi, twist):
    """The arm swung about its shoulder: out to its side by phi from hanging,
    round the vertical by yaw, rolled by twist."""
    swing = rot('Z', side * yaw) @ rot('Y', -side * phi)
    roll = Matrix.Rotation(math.radians(side * twist), 4, r.axis[arm])
    return T(r.head[arm]) @ swing @ roll @ r.rest3[arm]


def make_swing(r, arm, side, name):
    action = r.new_action(name)
    r.legs_free(False)
    frames = track(SWING)
    penetration = 0.0
    hits = []
    prev = None
    for f, p in enumerate(frames):
        m = arm_pose(r, arm, side, p['yaw'], p['phi'], p['twist'])
        r.apply({arm: m})
        r.key([arm], f)
        penetration = max(penetration, body_clearance(r, m))
        for h in SWING_HITS:
            if prev is not None and prev > h >= p['yaw']:
                hits.append(f)
        prev = p['yaw']
    for f in hits:
        action.pose_markers.new('hit').frame = f
    action.pose_markers.new('shake').frame = hits[0]
    return action, hits, penetration


def body_clearance(r, m):
    """How far the arm (matrix `m`), past its first metre, sinks into the
    core's box, m: 0 when clear."""
    pts = r.corners['Body']
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    worst = 0.0
    for t in (1.0, 1.4, 1.8, 2.2, 2.6, 3.0, 3.4):
        q = m @ Vector((0, t, 0))   # along the bone's own length
        inside = min(q.x - lo.x, hi.x - q.x, q.y - lo.y, hi.y - q.y, q.z - lo.z, hi.z - q.z)
        worst = max(worst, inside)
    return worst


# ---------------------------------------------------------------- the leap

def smooth01(x):
    x = max(0.0, min(1.0, x))
    return x * x * (3.0 - 2.0 * x)


def ground_pitch(r, arm, side, body, p):
    """The arm's pitch that puts its lowest point GROUND_CLEAR above the ground."""
    lo, hi = -100.0, 0.0
    for _ in range(40):
        mid = 0.5 * (lo + hi)
        m = arm_matrix(r, arm, side, body, mid, p['spread'])
        if r.low(arm, m) > GROUND_CLEAR:
            lo = mid
        else:
            hi = mid
    return 0.5 * (lo + hi)


def arm_matrix(r, arm, side, body, th, spread):
    shoulder = body @ r.head[arm]
    return T(shoulder) @ rot('X', th) @ rot('Y', -side * spread) @ r.rest3[arm]


def leg_pose(r, side, hip_m, lift, bend_forward):
    """Thigh, shin and foot for one leg, the ankle held at `lift` above its
    standing place and the knee found by two-bone IK."""
    s = 'L' if side > 0 else 'R'
    thigh, shin, foot = 'Thigh' + s, 'Shin' + s, 'Foot' + s
    hip = hip_m @ r.head[thigh]
    ankle = r.head[foot] + Vector((0, 0, lift))
    l1, l2 = r.length[thigh], r.length[shin]
    to = ankle - hip
    d = min(to.length, l1 + l2 - 1e-3)
    u = to.normalized()
    a = (l1 * l1 - l2 * l2 + d * d) / (2 * d)
    h = math.sqrt(max(l1 * l1 - a * a, 0.0))
    # The knee bows the way it stands at rest, and forward as the leg folds.
    rest_knee = r.head[shin]
    rest_to = r.head[foot] - r.head[thigh]
    rest_u = rest_to.normalized()
    rest_bow = (rest_knee - r.head[thigh]) - rest_u * (rest_knee - r.head[thigh]).dot(rest_u)
    rest_bow.normalize()
    fold = smooth01((r.length_rest[side] - d) / 1.0)
    want = rest_bow * (1 - fold) + Vector((0.25 * side, -1.0, 0)) * fold
    want = want - u * want.dot(u)
    if want.length < 1e-4:
        want = Vector((0, -1, 0))
    v = want.normalized()
    knee = hip + u * a + v * h
    thigh_m = T(hip) @ r.axis[thigh].rotation_difference(knee - hip).to_matrix().to_4x4() @ r.rest3[thigh]
    shin_m = T(knee) @ r.axis[shin].rotation_difference(ankle - knee).to_matrix().to_4x4() @ r.rest3[shin]
    foot_m = T(ankle) @ r.rest3[foot]
    return {thigh: thigh_m, shin: shin_m, foot: foot_m}, to.length > l1 + l2 - 1e-3


def make_leap(r, name):
    action = r.new_action(name)
    r.legs_free(True)
    # Standing leg reach, for how far folded a leg is.
    r.length_rest = {}
    for side in (1, -1):
        s = 'L' if side > 0 else 'R'
        r.length_rest[side] = (r.head['Foot' + s] - r.head['Thigh' + s]).length
    frames = track(LEAP)
    overreach = 0
    lowest = {'Body': 9.0, 'ArmL': 9.0, 'ArmR': 9.0, 'FootL': 9.0, 'FootR': 9.0}
    names = ['Body', 'Head', 'ArmL', 'ArmR', 'ThighL', 'ShinL', 'FootL', 'ThighR', 'ShinR', 'FootR']
    for f, p in enumerate(frames):
        body = T((0, 0, p['rise'])) @ about(rot('X', p['lean']), HIP)
        desired = {'Body': body @ r.rest['Body'],
                   'Head': body @ about(rot('X', -0.7 * p['lean']), r.head['Head']) @ r.rest['Head']}
        for arm, side in (('ArmL', 1), ('ArmR', -1)):
            th = p['th']
            if p['ground'] > 0:
                th += (ground_pitch(r, arm, side, body, p) - th) * p['ground']
            desired[arm] = arm_matrix(r, arm, side, body, th, p['spread'])
        lift = max(0.0, p['rise'] - 0.06) + p['tuck']
        for side in (1, -1):
            parts, over = leg_pose(r, side, body, lift, True)
            desired.update(parts)
            overreach += over
        r.apply(desired)
        r.key(names, f)
        for n in lowest:
            lowest[n] = min(lowest[n], r.low(n, desired[n]))
    action.pose_markers.new('hit').frame = LEAP_IMPACT
    action.pose_markers.new('shake').frame = LEAP_IMPACT
    return action, overreach, lowest


# ---------------------------------------------------------------- main

dill_mech.register()
bpy.ops.wm.open_mainfile(filepath=BLEND)
bpy.context.scene.render.fps = FPS
bpy.context.scene.render.fps_base = 1.0
r = Rig()
print('[melee] rig', r.rig.name, 'project', r.rig.get('dill_project'))

results = {}
for arm, side, name in (('ArmL', 1, 'swing_l'), ('ArmR', -1, 'swing_r')):
    action, hits, pen = make_swing(r, arm, side, name)
    path, claimed = export.export_action(r.rig, action, 0, len(track(SWING)) - 1)
    results[name] = (path, claimed, SWING_BODY)
    print(f'[melee] {name}: hits at frames {hits}, arm sinks {pen:.2f} m into the core at worst; claims {claimed}')

action, over, lowest = make_leap(r, 'leap_slam')
path, claimed = export.export_action(r.rig, action, 0, len(track(LEAP)) - 1)
results['leap_slam'] = (path, claimed, LEAP_BODY)
print('[melee] leap_slam: legs overreached on', over, 'frames; lowest points (m):', {k: round(v, 2) for k, v in lowest.items()}, 'claims', claimed)

# Body lines, written once: the lab owns them from then on.
for name, (path, claimed, body) in results.items():
    with open(path, encoding='utf-8') as f:
        text = f.read()
    if 'BodyAuto' not in text:
        with open(path, 'a', encoding='utf-8', newline='\n') as f:
            f.write(''.join(line + '\n' for line in body))

r.rig.animation_data.action = None
for pb in r.pose:
    pb.matrix_basis = Matrix.Identity(4)
    for c in pb.constraints:
        if c.type in {'IK', 'COPY_ROTATION'}:
            c.influence = 1.0
bpy.ops.wm.save_as_mainfile(filepath=BLEND)
print('[melee] saved', BLEND)
