# Vine nest generator

An offline look at a colony of giant pickle vines before any of it is worth
modelling by hand. Open `index.html` in a browser. Nothing is served, nothing
is downloaded, and Godot never sees this folder (`tools/.gdignore`).

The page holds an **archetype**: a seed, a voxel size, a few **kinds** of vine
and a **nest**. Every setting of a kind and of the nest is a range, not a
value. A kind's **pool** is a handful of seeds, and each seed rolls each
setting inside its range, so no two pool vines are alike. The nest lays the
colony out on the test planet and fills it with copies from the pools. This is
the project's rule for generation (`docs/decisions/0018`): authored pools and
one seed. In the game each pool vine is one `.vox`, and the nest is a list of
where they stand.

Chrome or Edge can write straight into a folder (Connect folder). Write pool +
nest then writes every pool vine as `prp_vine_<kind>_<n>.vox` with its settings
in a `.json` beside it, plus `vine_nest.json`, `vine_archetype.json` and
`vine_report.json`. Save archetype keeps just the ranges; Load reads an
archetype back, or any older single-vine preset.

## Ranges

Each range has two thumbs, low and high, and two boxes. Set both ends to the
same number for a fixed value. Each setting rolls from its own named stream of
the vine's seed, so widening one range never reshuffles the others, and the
same archetype seed always grows the same vines. Reroll picks a new seed.
On and off settings (tendrils, a leaf at the arm tips) are a chance instead.

## Kinds

A kind is a kind of vine: a name, a weight, a zone, a pool size, and a range
for every vine setting. The zone is where in the nest it grows, as a share of
the nest's radius: 0 is the heart, 1 the edge. Where zones overlap, the weights
decide. A pool vine's seed comes from the archetype's seed and the kind's
name, so renaming a kind gives it a new pool and reordering kinds does not.

The defaults:

| Kind | Zone | What it is |
|---|---|---|
| Heart | 0 | The great vine at the centre: 100–110 m, about 220 pods. |
| Fruiting | 0.05–0.45 | The nursery round the heart: tall, fruit on every arm, no leaves along them. About 190 pods. |
| Weeping | 0.35–0.8 | Long arms bowed into curtains of fruit, a leaf or two along them. About 130 pods. |
| Leafy | 0.75–1 | The outer wall: low, big leaves on the stalk and the arms, a few pods. Leans 10–16°. |

Heights step down from the heart, so the nest rises to a dome.

**Kind** shows the selected kind's pool side by side on the floor, with each
vine's height, voxels, pods and the share of pods hung clear. Warnings are
grouped by which vines they apply to.

## What a vine is

One object, named `Stalk`: the stalk, the knuckles, the arms, the pod stems,
the leaves and the tendrils. Godot turns each MagicaVoxel object into its own
destructible part, and a static part is held by the bottom of its own grid, so
a stalk split between rings would leave the top standing after the base was
cut. Pods are not voxels. Each attachment is a one-voxel object `@Pod01` …
that the importer turns into a `Marker3D` and does not draw. Palette index 255
is left empty. The feet sit on MagicaVoxel's ground, `z = 0`.

The stalk is solid. The outside is shell (`_diffuse`) and the inside is a pale
core (`_glass`), so a carve shows the core. Where the tip is thinner than
twice the shell, it is shell only. Voxel size defaults to 0.5 m, the same as
the trees: an 80 m vine is about 160 voxels tall, inside MagicaVoxel's 256
limit on each axis. A vine that passes 256 is reported and not written.

The tall tree that already spikes collision is about 30,000 voxels
(`docs/decisions/0024`); a pool vine past that is shown in red. Leaves are
the expensive part: a 12 m leaf is several hundred voxels.

A pod is the top of a hanging pickle: the game hangs a 1.8 m agent
(`HordeStats.Height`) below each marker. The page counts the pods with that
much clear air beneath them and warns about the ones that would hang into the
vine, another pickle or the ground, and about two pods closer than 0.9 m.

### Vine settings

**Vine.** Height (m), growth rings, bare stalk (m of trunk below the first
ring, room to walk or ride under), wobble (m) and how many waves of it, lean
(degrees), helix turns and the trellis radius (m), tip nod (how far the top
quarter bends over) and irregularity (0 is a perfect spiral; it jitters ring
spacing, arm angle, length, rise and sag, and pod spacing). The wobble fades in
over at least three times its own size, so the foot stays planted.

**Stalk.** Base radius and tip radius (m), shell thickness (voxels), knuckle
bulge (a fraction of the local radius) and knuckle width (voxels).

**Arms.** How many grow from each knuckle (0–4), length (m), rise (the angle
they leave at), sag (how far the fruit bows them down), thickness (a fraction
of the stalk's radius there) and the chance of a leaf at each tip. Arms are
solid, shell and core, and rooted at the stalk's axis. Zero arms hangs the
pods on the stalk.

**Pods.** Pods per ring, twist (degrees added each ring, so arms and pods
spiral) and the stem (m) each pickle hangs from. With arms, a ring's pods are
shared among its arms and spaced by how far out they hang along the outer
60 %, not by length along the arm, so they do not stack where a tip turns down.

**Leaves.** How many on the stalk per ring (0–2) and along each arm (0–6),
length (up to 24 m), tilt and droop (degrees). Stalk leaves sit midway between
the arms; arm leaves alternate sides, beside the fruit rather than on it. Five
lobes, one voxel thick.

**Tendrils.** The chance of any, how many, length (m), curl (turns). With arms
they curl off the arms' outer third.

## The nest

**Nest** lays the colony out on the 1200 m test planet, each vine stood on the
sphere with its own up.

- **The heart** stands at the centre (or a tight cluster of one to four), in an
  open court.
- **Rings** go round the court at the ring spacing, vines along each at the
  vine spacing, each ring staggered half a slot from the one inside so the next
  vine is always on a diagonal. Ring fill leaves a share of slots empty; jitter
  moves each trunk a little.
- **Avenues** run straight from the edge to the court, wide enough to ride. Near
  the heart an avenue narrows to a gate a third of the angle between avenues,
  so it cannot empty the small inner rings.
- **Lean to the heart** turns each vine so its own lean points inwards: the
  leafy outer wall closes over the court like the sides of a basket. It is a
  yaw only; nothing is tilted.

From above, the avenues and rings read from the descent. The canopy closes over
the avenues at the top, so they are lanes on the ground and swings above.

The grapple check: a swing links two vines whose anchors (a share of each
vine's height, 0.7 by default) are within the swing reach, never more than the
grapple's 140 m (`GrappleTuning.GrappleRange`). The panel reports how many
swings it takes from the outer ring to the heart, whether the whole nest is
one network, the widest gap from any vine to its nearest neighbour, and the
nearest two trunks. Grapple swings draws the links.

The panel also adds up the hanging pickles against 0029's 23,750 (95 % of
25,000) and the voxels of every placed vine.

| Preset | Radius | Vines | Pickles | Voxels |
|---|---|---|---|---|
| `nest_default.json` | 190–220 m | 75–124 | 7,700–11,400 | 1.4–2.2 M |
| `nest_dense.json` | 240–260 m | 174–228 | 22,400–24,000 | 3.2–4.5 M |

Measured over seeds 1–8 (default) and 1–4 (dense). Every seed of both is one
network, the heart three or four swings in.

## Output

`vine_nest.json` is what a game builder would read: each site's vine file,
kind, ring (-1 is the heart), `x` and `z` in metres on the plane tangent at the
nest's centre, and `rotationY`, Godot's, in radians. Nothing in the game reads
it yet.

## Preview

Pod markers are drawn as the pickles that will hang from them, 1.8 m tall.
Each vine is drawn without its hidden inside, and coarser past 260 m and 900 m;
pickles are drawn within 420 m.

| Control | What it does |
|---|---|
| Scroll | Dolly. Scroll up moves closer. |
| Middle mouse | Pan |
| Right mouse | Freelook. Mouse looks, WASD moves, Q and E are down and up, Shift is faster, the wheel changes speed. |
| Alt + left mouse | Orbit the point under the cursor |
| Kind | The selected kind's pool on the floor |
| Nest | The colony on the planet |
| Frame, or F | Frame what is shown |
| Orbit | Pull back to about 2.4 planet radii |

`index.html#nest` opens on the nest, `index.html#kind=2` on the third kind.

## Presets

`presets/` holds archetypes worth starting from, and three single vines from
before ranges (`vine_weeping`, `vine_laden`, `vine_leaning`); those load as one
kind whose ranges are that vine's values and whose seed is kept, so it grows
the same vine.

## Import

Copy a pool vine's `.vox` into `art/voxels/`. On the Import dock set
**voxel/size** to the archetype's (0.5 unless you changed it) and reimport.
Wrap it like `scenes/props/prp_tree_small_a.tscn`: a `VoxelBody`, `Static` on,
stats `resources/voxel/voxel_destruction_tree.tres`. The hanging pickles are
instanced on the `Pod` markers; this tool does not build them.

## Checks

From this folder:

```
node run-tests.js
```

The vine: the default builds with no warnings, a uniquely named marker for
every pod, no two in one cell, every pickle hung clear above the bare stalk, a
core inside the shell, knuckles fatter than the span beside them, a spiral of
pods, bounds MagicaVoxel can store, one seed encodes to the same bytes twice,
and an old preset grows its old vine. The ranges: every roll inside its range,
the same seed the same pool, one range widened moves no other setting, a
leafy vine mostly leaf and a fruiting one mostly pods, an archetype survives
saving. The nest: the same seed the same layout, the heart at the centre, no
trunk in an avenue, every vine in its kind's zone, the heart reachable by
swinging from the edge, one network, and a short reach reported.
