// The menu planet's cloud deck: a shell of cloud between two spheres, marched
// through, so each cloud has a real thickness: a flat belly, a domed top and a
// lit side and a shaded side that follow the sun. Cel shaded: three flat tones,
// no soft alpha. Opaque and cut out, the same rule as the game's smoke.
//
// Drawn on the outer sphere (PlanetLayer, AboveSurface is the cloud tops) and
// run through to the inner one (PlanetLayer.BaseAboveSurface is the cloud
// bellies). For each pixel the sight line is stepped through the shell until it
// meets cloud; from there the sun is looked for through a few more steps, and the
// cloud in the way decides the tone.
//
// The pattern is read on the position, not the surface, so height is a third
// dimension of the noise. The weather (planet_weather.gdshaderinc) turns that
// position round storms, bands and the poles.
//
// Cost (the menu's 60 fps on Iris Xe): a pixel reads the deck twenty or thirty
// times, so nothing is worked out per read that can be looked up. The weather is
// drawn into a map once a frame and the noise is a fixed 3D texture of the same
// lattice (CloudMaps and NoiseLattice, the globals planet_cloud_flow and
// planet_noise); without a CloudMaps in the scene the deck is wrong. The walk
// takes as many steps as the sight line has shell to cross, few looking down and
// many at the horizon, where too few slice the deck into stacked sheets. The
// cloud test stops adding noise once the answer cannot change.

shader_type spatial;
render_mode unshaded, cull_back;
// Marks its pixels, so the air hazes them as far as the deck (planet_atmosphere_clouds).
stencil_mode write, compare_always, 1;

#include "res://shaders/planet_weather.gdshaderinc"

global uniform sampler2D planet_cloud_flow : filter_linear, repeat_enable;
global uniform sampler3D planet_noise : filter_linear, repeat_enable;
global uniform sampler3D planet_puffs : filter_linear, repeat_enable;

instance uniform float planet_radius = 5000.0;
instance uniform float layer_radius = 5320.0;
instance uniform float base_height = 120.0;
instance uniform vec3 sun_direction = vec3(0.0, 1.0, 0.0);

// How much of the sky the clouds cover, how big their shapes are (shapes round
// the planet), and where in the noise they are.
uniform float coverage : hint_range(0.0, 1.0) = 0.5;
uniform float scale = 7.0;
uniform vec3 offset = vec3(0.0);

// Shape. Taper is how much denser the air has to be to go higher, so tops are
// domes and the clouds are flat on the bottom; Puff is the share of the detail
// noise, which makes the edge cauliflower.
uniform float taper : hint_range(0.0, 2.0) = 0.7;
uniform float puff : hint_range(0.0, 1.0) = 0.5;
// Dome is how much harder the air finds it to rise near the top than lower down:
// only the thick cores reach high, so a cloud is a heap with rounded tops and
// thin low edges, not a slab cut flat by the top of the shell (which nothing
// reaches anyway: the last quarter fades every cloud out).
uniform float dome : hint_range(0.0, 2.0) = 0.4;
// Billows: round lobes heaped over the tops and edges, more of them higher up,
// from packed balls (CloudPuffs) that drift with the deck but are not drawn out
// by the jets, so they stay round where the clouds are streaked. Billow is how
// far they bulge the cloud, billow scale how many (balls per shape). Billow
// middle is how deep inside a ball counts as nothing: shallower than it the
// ball carves the cloud (the creases), deeper it heaps it.
uniform float billow : hint_range(0.0, 1.0) = 0.42;
uniform float billow_scale : hint_range(1.0, 8.0) = 5.0;
uniform float billow_middle : hint_range(0.0, 1.0) = 0.3;
// The balls heap only where the column under them is cloud: billow gate is how
// far short of cloud a column can be and still be heaped on. Without it balls
// stood alone in the clear sky, popcorn. Carving is everywhere.
uniform float billow_gate : hint_range(0.01, 0.5) = 0.1;

// Light. The three tones, where the tones break, and how far toward the sun a
// cloud is looked through (a share of the shell's thickness).
uniform vec3 cloud_colour : source_color = vec3(0.97, 0.98, 1.0);
uniform vec3 mid_colour : source_color = vec3(0.74, 0.82, 0.95);
uniform vec3 shade_colour : source_color = vec3(0.46, 0.55, 0.78);
uniform float lit_from : hint_range(0.0, 1.0) = 0.62;
uniform float mid_from : hint_range(0.0, 1.0) = 0.3;
uniform float light_reach : hint_range(0.1, 2.0) = 0.9;
// How much the belly of a cloud is darker than its top.
uniform float belly_shade : hint_range(0.0, 1.0) = 0.3;
// How much the way the cloud's skin faces counts against the cloud in its way.
uniform float facing_weight : hint_range(0.0, 1.0) = 0.8;
// How much brighter a billow's crown is than the crease between billows, so
// each lobe reads as a round lump of its own.
uniform float crease_shade : hint_range(0.0, 1.0) = 0.7;
// What the night side of the deck is multiplied by.
uniform vec3 night_colour : source_color = vec3(0.12, 0.16, 0.32);

// The longest step through the shell, as a share of its thickness: smaller is
// finer at the horizon and dearer there.
uniform float step_share : hint_range(0.05, 1.0) = 0.35;
// Where the sight line skims the deck its steps are longer than a ball's thin
// rim, which falls between them on some pixels and not others: the edges came
// apart into lines along the horizon. There the clouds are grown by this much
// (in coverage), so a step catches the rim; far off they read a little fuller.
uniform float horizon_grow : hint_range(0.0, 0.3) = 0.06;
// Seen edge-on at the horizon a ball is thinner than a row of pixels, so each
// row meets a different one: the deck came apart into lines however fine the
// steps. There the balls and the finest noise fade by this share, leaving the
// banks' own round shapes.
uniform float horizon_smooth : hint_range(0.0, 1.0) = 0.8;
// The least and most steps a sight line takes.
const int MIN_STEPS = 4;
const int MAX_STEPS = 96;
// The noise texture's edge, in lattice cells (NoiseLattice.Edge).
const float NOISE_EDGE = 128.0;
// The balls texture's edge, in balls (CloudPuffs.Cells).
const float PUFF_CELLS = 8.0;
// The balls are turned off the planet's axes, so their texture's repeat does
// not line up with the poles or the equator.
const mat3 PUFF_TURN = mat3(vec3(0.855392, 0.353339, -0.378757), vec3(-0.201275, 0.900490, 0.385496), vec3(0.477277, -0.253516, 0.841390));

varying vec3 world_position;
// The deck's drift this frame as a sine and cosine, for the billows.
varying flat vec2 drift_turn;

// PCG's output hash: white even between neighbouring integers.
uint pcg(uint v) {
	uint state = (v * 747796405u) + 2891336453u;
	uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
	return (word >> 22u) ^ word;
}

// Value noise on the integer lattice, smoothed between cells. The lattice's
// values are the texture's texels and the hardware blends between them; reading
// at the eased position gives the same smooth noise the hash did.
float value_noise(vec3 p) {
	vec3 i = floor(p);
	vec3 f = p - i;
	f = f * f * (3.0 - 2.0 * f);
	return textureLod(planet_noise, (i + f + 0.5) / NOISE_EDGE, 0.0).r;
}

// The weather, read from its map: where the pattern at q (centred on the
// planet, a planet radius long) is read from.
vec3 flow(vec3 q) {
	float l = length(q);
	float edge = float(textureSize(planet_cloud_flow, 0).x);
	vec2 uv = weather_uv(weather_fold(q / l), edge);
	vec3 turned = (textureLod(planet_cloud_flow, uv, 0.0).xyz * 2.0) - 1.0;
	return normalize(turned) * l;
}

// How high in the shell, 0 at the bellies and 1 at the tops.
float shell_height(float r) {
	return clamp((r - planet_radius - base_height) / max(layer_radius - planet_radius - base_height, 1.0), 0.0, 1.0);
}

// How much denser the air has to be at height h to hold a cloud.
float rise(float h) {
	return (taper * h) + (dome * h * h) + (0.7 * smoothstep(0.75, 1.0, h));
}

// How far the billows can push the density either way at height h.
float billow_reach(float h) {
	return 0.75 * billow * (0.15 + (0.85 * h));
}

// The billows at p, between -billow_reach(h) and +billow_reach(h): how deep p
// is in the packed balls, round on top and creased where two meet, read on the
// planet turned only by the drift, times detail (horizon_smooth). One read: a
// second cost a millisecond.
float billows(vec3 p, float h, float detail) {
	float s = drift_turn.x;
	float c = drift_turn.y;
	vec3 d = p / planet_radius;
	vec3 q = PUFF_TURN * vec3((d.x * c) + (d.z * s), d.y, (d.z * c) - (d.x * s)) * (scale * billow_scale);
	float inside = textureLod(planet_puffs, q / PUFF_CELLS, 0.0).r;
	return clamp((inside - billow_middle) * 2.0, -1.0, 1.0) * billow_reach(h) * detail;
}

// The billows b where the column under them is as cloudy as column (its two
// coarse noises against the coverage, before the height takes its share): the
// heaping held to cloudy columns, the carving left alone.
float gated(float b, float column) {
	return b > 0.0 ? b * smoothstep(-billow_gate, 0.0, column) : b;
}

// Above zero is cloud. p is centred on the planet; detail is the share of the
// balls and the finest noise kept (less at the horizon).
float density(vec3 p, float detail) {
	vec3 q = (flow(p / planet_radius) * scale) + offset;
	float h = shell_height(length(p));
	// Big slow noise first, so the clouds gather into banks with clear sky between
	// them; the finer noise only roughens the edge of a bank.
	float column = (value_noise((q * 0.3) + 13.0) * 0.5) + (value_noise(q) * 0.3) - (1.0 - coverage);
	float n = column + (value_noise(q * 2.07) * 0.12) + (value_noise(q * 4.3) * (0.08 * puff * detail));
	return n + gated(billows(p, h, detail), column) - rise(h);
}

// The same test as density() > 0, cheaper: every noise adds between nothing and
// its share (the billows as much either way), so once the sum is sure to stay
// under the line, or is already over it, the finer noise is not read.
// Grow adds to the coverage (horizon_grow, where the sight line skims).
bool is_cloud(vec3 p, float grow, float detail) {
	vec3 q = (flow(p / planet_radius) * scale) + offset;
	float h = shell_height(length(p));
	float fine = 0.12 + (0.08 * puff * detail);
	float lobe = billow_reach(h) * detail;
	float n = (value_noise((q * 0.3) + 13.0) * 0.5) - (1.0 - coverage - grow) - rise(h);
	if (n + 0.3 + fine + lobe <= 0.0) {
		return false;
	}
	n += value_noise(q) * 0.3;
	if (n - lobe > 0.0) {
		return true;
	}
	if (n + fine + lobe <= 0.0) {
		return false;
	}
	n += gated(billows(p, h, detail), n + rise(h));
	if (n > 0.0) {
		return true;
	}
	if (n + fine <= 0.0) {
		return false;
	}
	n += value_noise(q * 2.07) * 0.12;
	if (n > 0.0) {
		return true;
	}
	return n + (value_noise(q * 4.3) * (0.08 * puff * detail)) > 0.0;
}

void vertex() {
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float angle = radians(drift_degrees_per_second) * TIME;
	drift_turn = vec2(sin(angle), cos(angle));
}

void fragment() {
	vec3 centre = MODEL_MATRIX[3].xyz;
	vec3 cam = (INV_VIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz - centre;
	vec3 ray = normalize(world_position - centre - cam);

	float r_in = planet_radius + base_height;
	float r_out = layer_radius;
	float b = dot(cam, ray);
	float cc = dot(cam, cam);

	float disc_out = (b * b) - (cc - (r_out * r_out));
	if (disc_out < 0.0) {
		discard;
	}
	float root_out = sqrt(disc_out);
	float t0 = max(-b - root_out, 0.0);
	float t1 = -b + root_out;
	// The belly sphere ends the walk where the sight line meets it.
	float disc_in = (b * b) - (cc - (r_in * r_in));
	if (disc_in > 0.0) {
		float t_in = -b - sqrt(disc_in);
		if (t_in > t0) {
			t1 = min(t1, t_in);
		}
	}

	// As many steps as the shell crossed needs: a sight line skimming the deck
	// at the horizon crosses kilometres of it.
	float thickness = r_out - r_in;
	float crossing = (t1 - t0) / max(thickness, 1.0);
	float skim = smoothstep(4.0, 8.0, crossing);
	// Slanting through the deck short of the horizon, the steps sweep across a
	// thin cloud in step from pixel to pixel: stripes. The jitter and the growth
	// start there, before the steps shorten and the clouds are smoothed (shorter
	// steps there too cost two milliseconds).
	float slant = smoothstep(2.5, 4.0, crossing);
	float steps = clamp(ceil((t1 - t0) / max(thickness * step_share * (1.0 - (0.5 * skim)), 1.0)), float(MIN_STEPS), float(MAX_STEPS));
	float dt = (t1 - t0) / steps;
	// Where the sight line skims the deck it crosses the clouds' thin edges
	// between steps, so the same edge is caught on one pixel and missed on the
	// next, in rows: lines along the horizon. There the steps are half as long and
	// start a fixed fraction further back per pixel (a hash of the pixel: white,
	// as a pattern with structure shows its own diagonals; PCG, as hash_bits on
	// neighbouring pixels was not white and drew diagonal hatching), so the
	// misses are grain, not rows, and the clouds there are grown so there are
	// few misses. Everywhere else the last step is on the bellies, so an edge
	// seen from above is caught the same on every pixel and stays clean.
	float dither = slant * float(pcg(uint(FRAGCOORD.x) + pcg(uint(FRAGCOORD.y))) >> 8u) * (1.0 / 16777216.0);
	float grow = horizon_grow * slant;
	float detail = 1.0 - (horizon_smooth * skim);
	float hit = -1.0;
	float previous = t0;
	for (int i = 0; i < MAX_STEPS; i++) {
		if (float(i) >= steps) {
			break;
		}
		// The last step stays on the bellies, jittered or not: a thin cloud low in
		// the deck is caught there on every pixel, not on some (speckle).
		float t = (float(i) + 1.0 >= steps) ? t1 : t0 + (dt * (float(i) + 1.0 - dither));
		if (is_cloud(cam + (ray * t), grow, detail)) {
			hit = t;
			break;
		}
		previous = t;
	}
	if (hit < 0.0) {
		discard;
	}
	// Close in on the surface between the last empty step and the first full one.
	float lo = previous;
	float hi = hit;
	for (int i = 0; i < 4; i++) {
		float mid = 0.5 * (lo + hi);
		if (is_cloud(cam + (ray * mid), grow, detail)) {
			hi = mid;
		} else {
			lo = mid;
		}
	}
	vec3 p = cam + (ray * hi);

	vec3 to_sun = normalize(sun_direction);
	float h = clamp((length(p) - r_in) / max(thickness, 1.0), 0.0, 1.0);

	// How much cloud lies between this point and the sun.
	float shut = 0.0;
	for (int i = 1; i <= 3; i++) {
		vec3 q = p + (to_sun * thickness * light_reach * (float(i) / 3.0));
		shut += is_cloud(q, grow, detail) ? 1.0 : 0.0;
	}
	// Which way the cloud's skin faces here: down the slope of the density. A
	// lump is then lit on the sun's side and shaded on the far side, a flat top
	// is bright and an underside dark, whatever else is in the way.
	float reach = max(thickness * 0.1, 1.0);
	// p is on the skin, where the density is nothing to a few metres.
	float here = 0.0;
	vec3 slope = vec3(
		density(p + vec3(reach, 0.0, 0.0), detail) - here,
		density(p + vec3(0.0, reach, 0.0), detail) - here,
		density(p + vec3(0.0, 0.0, reach), detail) - here);
	vec3 skin = -slope / max(length(slope), 0.00001);
	float facing = (dot(skin, to_sun) * 0.5) + 0.5;
	float open_sky = 1.0 - (shut / 3.0);
	float lit = mix(open_sky, open_sky * facing, facing_weight);
	float tone = (lit * (1.0 - belly_shade)) + (h * belly_shade);
	float lobe = (billows(p, h, 1.0) / max(billow_reach(h), 0.0001)) * detail;
	tone += crease_shade * 0.5 * lobe;

	vec3 colour = mix(shade_colour, mid_colour, step(mid_from, tone));
	colour = mix(colour, cloud_colour, step(lit_from, tone));

	float day = smoothstep(-0.15, 0.3, dot(normalize(p), to_sun));
	colour *= mix(night_colour, vec3(1.0), day);

	ALBEDO = colour;
}
