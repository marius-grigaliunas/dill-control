// The sky of a planet and its system: a zenith-to-horizon ramp measured from
// the eye's own up, a ground band below the real limb, a static cloud deck,
// stars that come in as the air thins, the sun disc from the level's light,
// and the system's other bodies as lit discs.
//
// PLACEHOLDER for the art pass. Ported from the lab's planet_sky, which was
// written for one planet with a moon mesh; the moon is gone and the bodies
// here cost no draw call: each is a disc found by one dot product, lit from
// the sun, and they are unreachable scenery (decision 0017).
//
// SolarSystem pushes up, dip and blend every frame as globals (smooth, and
// they do not dirty the Sky) and, throttled, as material uniforms for the
// radiance cubemap pass. A biome sets the colours.
shader_type sky;

uniform vec3 up_direction = vec3(0.0, 1.0, 0.0);

// The same three camera-derived values as up_direction, horizon_dip and
// atmosphere_blend, but as *global* uniforms, written every frame.
//
// The material uniforms have to be throttled: writing one marks the Sky dirty
// and rebuilds its radiance cubemap. But throttled means stepped, and the
// clouds sit on a deck whose parallax is measured from the eye -- so every
// quarter-degree push jumped the whole deck by the five metres the eye had
// moved since the last one. Reported as "the clouds are stepping all the time
// ... choppy, even though there's 120 fps". A global scalar changes a shared
// buffer and does not dirty the Sky, so the on-screen pass reads these,
// smooth, every frame, and the cubemap pass keeps reading the throttled
// material values -- it is a blurred lighting term and never needed better.
// Declared in project.godot under [shader_globals].
global uniform vec3 sky_eye_up;
global uniform float sky_horizon_dip;
global uniform float sky_atmosphere_blend;

// Darker than a full daytime blue on purpose: this is a small world with two
// visible bodies in its sky, reported as "we still see the blue sky" and not
// reading as a space game at all. A thin-atmosphere zenith -- dark enough for
// stars and the moon to read against it, still with a horizon glow so there
// is unmistakably ground and air rather than a vacuum -- is the cheap fix
// available to a two-colour gradient shader; a real scattering model is the
// art pass's job.
uniform vec3 zenith_color : source_color = vec3(0.03, 0.05, 0.13);
uniform vec3 horizon_color : source_color = vec3(0.5, 0.62, 0.78);
uniform vec3 ground_color : source_color = vec3(0.28, 0.26, 0.23);

// How tightly the horizon band hugs the horizon. Higher is a sharper break.
uniform float horizon_falloff : hint_range(1.0, 24.0) = 6.0;
uniform float ground_falloff : hint_range(1.0, 24.0) = 8.0;

uniform float sun_disc_energy : hint_range(0.0, 60.0) = 22.0;
uniform float sun_glow_energy : hint_range(0.0, 8.0) = 2.4;

// The warm smear around the sun that sells "there is air here".
uniform vec3 sun_halo_color : source_color = vec3(1.0, 0.86, 0.62);
uniform float sun_halo_energy : hint_range(0.0, 4.0) = 0.9;

// Clouds. Static rather than scrolling: the Sky resource runs INCREMENTAL, so
// its radiance cubemap is rebuilt a slice at a time, and a sky that changes
// every frame would either be rebuilt every frame or lag its own lighting.
// Nothing here samples TIME.
uniform vec3 cloud_color : source_color = vec3(1.0, 0.99, 0.97);
uniform vec3 cloud_shadow_color : source_color = vec3(0.62, 0.66, 0.74);

// Noise cells per radian of the deck, measured from the planet's centre. At a
// 1200 m planet and the deck below, 15 is a cell of roughly a hundred metres.
uniform float cloud_scale = 15.0;

// Height of the cloud deck above the surface, as a fraction of the planet's
// radius. The deck is a real sphere: clouds stay put over the ground as you
// move under them, and the ones near the horizon are far away because they
// are. A fraction rather than metres so it survives the radius moving.
uniform float cloud_deck_height : hint_range(0.02, 1.0) = 0.2;
uniform float cloud_coverage : hint_range(0.0, 1.0) = 0.46;
uniform float cloud_softness : hint_range(0.01, 1.0) = 0.18;
uniform float cloud_opacity : hint_range(0.0, 1.0) = 0.85;

// How far above the local horizon the cloud deck starts to be visible. Clouds
// drawn all the way down to the horizon read as fog, not as sky.
uniform float cloud_horizon : hint_range(0.0, 1.0) = 0.06;

// A sparse, static star field -- part of the same "look like a space game"
// fix as the darker zenith above. Static per direction rather than animated:
// this Sky runs INCREMENTAL (see the file comment on up_direction), so a
// twinkle driven by TIME would rebuild against a moving target every slice.
// "Twinkle" here is really per-star brightness variance, fixed at bake time,
// not motion.
uniform vec3 star_color : source_color = vec3(1.0, 0.98, 0.92);
uniform float star_density : hint_range(0.9, 0.9999) = 0.9935;
uniform float star_brightness : hint_range(0.0, 4.0) = 1.6;

// How much atmosphere is left: 1 while standing on a body, 0 out in the void
// between them. Pushed once a frame from SpikeDirector.ComputeAtmosphereBlend,
// throttled the same way up_direction already is (see that uniform's own
// comment) so it does not keep this INCREMENTAL Sky permanently dirty. It
// defaults to 1 rather than 0 so a shader instance nobody is pushing into --
// an editor preview, a level that never wires SpikeDirector to this sky --
// still shows today's atmosphere rather than going black.
uniform float atmosphere_blend : hint_range(0.0, 1.0) = 1.0;

// How far below the eye-level plane the planet's limb sits, as the sine of
// that angle: sqrt(1 - (R / d)^2) for an eye at distance d from the centre.
// Pushed with atmosphere_blend. Without it the ground band sat on the
// eye-level plane at every altitude, so climbing out painted a flat grey
// "ground" over the whole lower half of the sky long after the real planet
// had shrunk to a disc below you -- which is where the stars went missing on
// the way out.
uniform float horizon_dip : hint_range(0.0, 1.0) = 0.0;

// The void colour. Not pure black: a hair of blue-black keeps the tonemapper
// (this level runs one) and the star field from clipping against a hard zero.
uniform vec3 space_color : source_color = vec3(0.01, 0.012, 0.02);

// Below this, the in-atmosphere branch (horizon gradient, ground band, clouds)
// is not merely dimmed but skipped outright -- see sky()'s own comment on why
// that distinction is the entire point of this uniform.
// ---- the system's other bodies ----
// Up to four. xyz is the unit direction toward a body's centre, w the cosine
// of its angular radius. Pushed once at build by SolarSystem.
uniform int body_count : hint_range(0, 4) = 0;
uniform vec4 body_dir_size[4];
uniform vec3 body_color[4] : source_color;
// How much a body shows through daylight, at full atmosphere.
uniform float body_day_visibility : hint_range(0.0, 1.0) = 0.35;

const float ATMOSPHERE_EPSILON = 0.003;

// Integer bit-mixing rather than the usual fract(p * 0.1031) float hash. Same
// reason as planet_terrain.gdshader: that hash correlates badly once cell
// indices get large, and the finer cloud octaves are exactly that case.
uint hash_bits(uvec3 q) {
	q *= uvec3(1597334673u, 3812015801u, 2798796415u);
	return (q.x ^ q.y ^ q.z) * 1597334673u;
}

// Takes the integer cell rather than a position, so value_noise steps its eight
// corners with an integer add instead of eight floor-and-convert chains. The
// +1024 that carries negative coordinates into unsigned range is applied once
// by the caller.
float hash_cell(uvec3 q) {
	return float(hash_bits(q)) * (1.0 / 4294967295.0);
}

// One hash per star cell rather than a texture: cheap, and it reuses the same
// integer-mixing hash the cloud noise above depends on.
//
// A star is a soft point near its cell's centre, not the whole cell. A lit
// cell was a hard-edged square about two pixels wide, and a square that size
// crawls and sparkles as the view turns -- reported as "the stars are
// glitching". The point is widened to at least a pixel's footprint, with its
// peak lowered to match, so a turning view moves it smoothly instead of
// flicking it between pixels.
float stars(vec3 dir, float pixel) {
	vec3 p = dir * 380.0;
	vec3 base = floor(p);
	uvec3 cell = uvec3(ivec3(base) + 3000);
	float n = hash_cell(cell);
	if (n < star_density) {
		return 0.0;
	}
	float variance = hash_cell(cell + uvec3(7919u, 0u, 0u));

	// The cell's centre, pulled onto the sphere the direction lies on, so the
	// point is where the ray can actually pass through it.
	vec3 centre = normalize(base + 0.5) * 380.0;
	float d = length(p - centre);

	float size = max(0.22, pixel * 0.75);
	float peak = (0.22 * 0.22) / (size * size);

	return mix(0.35, 1.0, variance) * smoothstep(star_density, 1.0, n)
		* exp(-(d * d) / (size * size)) * min(1.0, peak * 2.0);
}

float value_noise(vec3 p) {
	vec3 i = floor(p);
	vec3 f = p - i;
	f = f * f * (3.0 - 2.0 * f);

	uvec3 c = uvec3(ivec3(i) + 1024);

	return mix(
		mix(mix(hash_cell(c), hash_cell(c + uvec3(1u, 0u, 0u)), f.x),
			mix(hash_cell(c + uvec3(0u, 1u, 0u)), hash_cell(c + uvec3(1u, 1u, 0u)), f.x), f.y),
		mix(mix(hash_cell(c + uvec3(0u, 0u, 1u)), hash_cell(c + uvec3(1u, 0u, 1u)), f.x),
			mix(hash_cell(c + uvec3(0u, 1u, 1u)), hash_cell(c + uvec3(1u, 1u, 1u)), f.x), f.y),
		f.z);
}

// Two octaves, not four. Four were costing a hash-heavy trilinear sample over
// most of the screen's background pixels on an integrated GPU -- the sky
// shader runs per background pixel every frame, not once like the terrain's
// noise, which only ever covers what ground is actually on screen. Two keeps
// the "not one flat blob" shape without the two most expensive samples.
float cloud_fbm(vec3 p) {
	float total = value_noise(p) * 0.65;
	total += value_noise(p * 2.4) * 0.35;
	return total;
}

// Everything below is branched by where on the sky the pixel is, or by
// atmosphere_blend. Both are deliberate optimisations rather than a style
// choice: this shader runs once per background pixel every frame, and on a
// 1200 m planet the 64 m horizon means the background is most of the screen.
// The height/cloud branches are coherent in screen space -- the horizon is a
// line, the sun is a disc -- so a wavefront almost always takes one side of it
// together. atmosphere_blend is coherent across the *entire frame*, since it
// depends only on camera position and not on EYEDIR at all: every pixel this
// frame takes the same side of that branch, which is what lets the deep-space
// case skip the cloud FBM -- this shader's single most expensive term --
// entirely rather than computing and discarding it.
void sky() {
	// Star-cell units per pixel, for the stars' antialiasing. Taken here, at
	// the top, because derivatives inside control flow that differs between
	// neighbouring pixels are undefined, and every star lookup is inside one.
	float star_pixel = max(length(fwidth(EYEDIR * 380.0)), 0.05);

	vec3 up = normalize(AT_CUBEMAP_PASS ? up_direction : sky_eye_up);
	float dip = AT_CUBEMAP_PASS ? horizon_dip : sky_horizon_dip;
	float blend = AT_CUBEMAP_PASS ? atmosphere_blend : sky_atmosphere_blend;

	// Measured from the planet's real limb rather than the eye-level plane:
	// 0 at the limb, positive above it, negative over the ground.
	float height = dot(EYEDIR, up) + dip;

	// Stars belong to thin air, not to the atmosphere. Reported the other way
	// round -- visible standing on the ground in daylight, missing on the way
	// out -- which was literal: the atmosphere branch drew them and the ground
	// band hid them. They come in as the air thins and are full well before
	// the atmosphere is gone, and inside the atmosphere only on the night
	// side, where the sun is below the local horizon and the sky is dark for
	// a reason rather than by decree.
	float thin_air = 1.0 - smoothstep(0.55, 0.95, blend);
	float night = 1.0;
	if (LIGHT0_ENABLED) {
		night = smoothstep(0.08, -0.12, dot(up, LIGHT0_DIRECTION));
	}
	float star_amount = max(thin_air, night * blend);
	if (height < 0.0) {
		// Behind the planet's bulk while there is atmosphere to draw it.
		star_amount *= 1.0 - blend;
	}

	float star = 0.0;
	// Not in the cubemap pass: a lighting term has no use for points of
	// light, and they would only shimmer through the ambient.
	if (star_amount > 0.001 && !AT_CUBEMAP_PASS) {
		star = stars(EYEDIR, star_pixel) * star_amount;
		if (LIGHT0_ENABLED) {
			float sun_dot = max(dot(EYEDIR, LIGHT0_DIRECTION), 0.0);
			star *= 1.0 - smoothstep(0.0, 0.35, sun_dot);
		}
	}

	vec3 color;

	if (blend > ATMOSPHERE_EPSILON) {
		vec3 atmo_color;

		// How much of this pixel the cloud deck covers. Stars are added after
		// the atmosphere, so without this they shone through the clouds.
		float cloud_cover = 0.0;

		if (height >= 0.0) {
			// Sky half: ramp from horizon colour to zenith colour. The pow is
			// what keeps the pale band thin and close to the horizon instead
			// of washing the whole upper hemisphere out.
			atmo_color = mix(horizon_color, zenith_color, pow(height, 1.0 / horizon_falloff));
		} else {
			// Ground half: the planet's own bulk, not black. Anything below
			// the local horizon is terrain we are not drawing, and a black
			// band there reads as a hole in the world rather than as ground.
			// Only meaningful while there is atmosphere to speak of -- see
			// the space branch below for why this band does not exist there.
			atmo_color = mix(horizon_color, ground_color, pow(-height, 1.0 / ground_falloff));
		}

		// ---- clouds ----
		// On a deck sphere around the planet, found by intersecting the view
		// ray with it -- not the eye direction divided by its own height, which
		// is what they were. That mapping made the whole field a function of
		// horizon_dip and up_direction, and both are pushed in steps as the
		// camera moves: a jump changes the dip by a fifth, walking over a rise
		// by more, and every push re-stretched every cloud in the sky at once.
		// Reported as "jumping makes the clouds change appearance ... flying at
		// the surface it just makes the clouds look glitchy". A deck fixed to
		// the planet only moves by parallax, which is what clouds do.
		//
		// Skipped outright in the radiance cubemap pass. The cubemap is what
		// lights the ground, it is rebuilt a slice at a time after every
		// parameter push, and a blurred ambient term has no use for cloud
		// detail -- keeping it in there is what turned each push into the
		// ground's brightness changing under the player.
		if (!AT_CUBEMAP_PASS) {
			// Units of the eye's distance from the centre, so the eye is at
			// `up` and the planet's radius is R/d, recovered from the dip.
			float planet = sqrt(max(1.0 - dip * dip, 0.0));
			float deck = planet * (1.0 + cloud_deck_height);
			float b = dot(EYEDIR, up);
			float c = 1.0 - deck * deck;
			float disc = b * b - c;

			float t = -1.0;
			float fade = 0.0;
			if (c < 0.0) {
				// Under the deck: every ray leaves through it. The fade at the
				// horizon and behind the viewer's feet is also what keeps the
				// noise off the pixels below the horizon, which is half the
				// sky when looking level -- sixteen integer hashes a pixel
				// that used to be evaluated and then multiplied away.
				// Measured from eye level, not from the limb, so the fade does
				// not move when the dip does.
				fade = smoothstep(cloud_horizon, cloud_horizon + 0.22, b);
				if (fade > 0.0) {
					t = -b + sqrt(disc);
				}
			} else if (disc > 0.0 && b < 0.0) {
				// Above the deck, looking down at it. Only where the ray passes
				// the planet by -- the ring of deck seen over the limb -- since
				// anything it would cover on the ground is drawn by the terrain.
				float ground = b * b - (1.0 - planet * planet);
				if (ground < 0.0) {
					t = -b - sqrt(disc);
					fade = 1.0;
				}
			}

			if (t > 0.0) {
				float density = cloud_fbm(normalize(up + EYEDIR * t) * cloud_scale);

				// Coverage is a threshold, softness is how far either side of
				// it the edge runs. Together they are overcast against scattered.
				float cloud = smoothstep(
					cloud_coverage,
					cloud_coverage + cloud_softness,
					density);

				cloud *= fade * cloud_opacity;

				// Lit on the sunward side, shadowed away from it, which is most
				// of what makes a flat noise field read as volume.
				float toward_sun = LIGHT0_ENABLED ? dot(EYEDIR, LIGHT0_DIRECTION) : 0.0;
				vec3 lit = mix(cloud_shadow_color, cloud_color, smoothstep(-0.25, 0.75, toward_sun));
				atmo_color = mix(atmo_color, lit, cloud);
				cloud_cover = cloud;
			}
		}

		// Fades the whole in-atmosphere look toward the void as blend falls,
		// rather than a hard cutover -- this is what makes leaving a planet a
		// fade instead of a pop. SpikeDirector owns the easing curve; this
		// shader only ever sees the already-eased result.
		color = mix(space_color, atmo_color, blend);

		// Added after the blend rather than inside atmo_color, so they are not
		// dimmed by the very atmosphere whose thinning brings them in. The
		// horizon band keeps them washed out near the limb.
		color += star_color * star * (1.0 - cloud_cover) * star_brightness * smoothstep(-0.02, 0.2, height + (1.0 - blend));
	} else {
		// Deep space: a uniform void in every direction, not just above the
		// horizon. There is no "ground half" out here -- the planet and the
		// moon are separate meshes that already occlude this shader correctly
		// regardless of distance, and what used to read as a horizon band at
		// any range was this shader's own ground_color hemisphere bleeding
		// through long after the real terrain mesh had shrunk to a dot.
		// Nothing blocks the lower hemisphere in a vacuum, so stars go in
		// both halves here, unlike the in-atmosphere branch above.
		color = space_color + (star_color * star * star_brightness);
	}

	// Bodies, behind the atmosphere's daylight but over the stars. Not in
	// the cubemap pass, for the same reason as the stars. The planet's own
	// mesh hides them where it covers the sky, so nothing here tests for it.
	if (!AT_CUBEMAP_PASS) {
		for (int i = 0; i < body_count; i++) {
			vec3 dir = body_dir_size[i].xyz;
			float edge = body_dir_size[i].w;
			float toward = dot(EYEDIR, dir);
			if (toward > edge) {
				// Where on the disc this pixel is, as a point on a unit sphere
				// facing the eye: x and y across the disc, z toward the eye.
				float sine = sqrt(max(1.0 - edge * edge, 0.000001));
				vec3 across = (EYEDIR - dir * toward) / sine;
				float z = sqrt(max(1.0 - dot(across, across), 0.0));
				vec3 normal = normalize(across - dir * z);
				float lit = LIGHT0_ENABLED ? max(dot(normal, LIGHT0_DIRECTION), 0.0) : 0.5;
				vec3 body = body_color[i] * (0.04 + lit * 0.96);
				float rim = smoothstep(edge, edge + (1.0 - edge) * 0.08, toward);
				float show = rim * mix(1.0, body_day_visibility, blend);
				if (height < 0.0) {
					show *= 1.0 - blend;
				}
				color = mix(color, body, show);
			}
		}
	}

	if (LIGHT0_ENABLED) {
		// LIGHT0_DIRECTION already points TOWARD the light, not along its
		// travel. This used to negate it, on the strength of a measurement made
		// against space_sky.gdshader, and it put the disc -- and everything else
		// here that asks where the sun is -- on the anti-sun side of the sky.
		// Re-measured in this level on 2026-09-27: looking along the Sun node's
		// +Z from the spawn shows the disc with this sign and nothing without it.
		float toward = dot(EYEDIR, LIGHT0_DIRECTION);

		// Every term here is identically zero looking away from the sun, which
		// is half the sky.
		if (toward > 0.0) {
			// Wider than a real sun on purpose: at half a degree it is one
			// pixel you can never find, and finding it is how you orient on a
			// planet that has no landmarks.
			float disc = smoothstep(0.9975, 0.9990, toward);

			// One logarithm for both powers. pow(x, n) is exp2(n * log2(x)),
			// and these two share their x.
			float log_toward = log2(max(toward, 0.000001));
			float glow = exp2(700.0 * log_toward);
			float halo = exp2(8.0 * log_toward);

			color += LIGHT0_COLOR * ((disc * sun_disc_energy) + (glow * sun_glow_energy));
			color += sun_halo_color * halo * sun_halo_energy;
		}
	}

	COLOR = color;
}
