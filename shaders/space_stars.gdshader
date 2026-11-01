// The stars behind the menu planet: points on the inside of a huge sphere
// (PlanetLayer, far out and wider than anything else), each a pixel or two
// across whatever the screen, in two sizes of cell so there are many faint ones
// and a few bright ones, tinted a little warm or cold and twinkling slowly.
// Unshaded and opaque, drawn behind everything by the depth test.

shader_type spatial;
render_mode unshaded, cull_front, depth_draw_always;

uniform float faint_density = 120.0;
uniform float bright_density = 45.0;
// The share of cells that hold a star.
uniform float faint_share : hint_range(0.0, 1.0) = 0.07;
uniform float bright_share : hint_range(0.0, 1.0) = 0.03;
uniform float brightness = 1.0;
uniform float twinkle : hint_range(0.0, 1.0) = 0.25;
uniform vec3 warm : source_color = vec3(1.0, 0.82, 0.62);
uniform vec3 cold : source_color = vec3(0.68, 0.8, 1.0);
// A faint haze of the galaxy: a band across the sky, and how strong.
uniform vec3 haze_colour : source_color = vec3(0.1, 0.12, 0.3);
uniform vec3 haze_axis = vec3(0.3, 1.0, 0.2);
uniform float haze_strength : hint_range(0.0, 1.0) = 0.35;

// The sun: the same direction the light shines from (PlanetLayer pushes it), so
// moving the Sun node moves the disc. A flat disc, two flat rings, a soft glow and
// a cross of streaks. It is drawn into the sky, so the planet hides it as it
// rises from behind.
instance uniform vec3 sun_direction = vec3(0.0, 1.0, 0.0);
// How it looks is the SkySun node's (they are instance uniforms, set from there).
instance uniform float sun_radius = 0.8;
instance uniform vec3 sun_colour : source_color = vec3(1.0, 0.95, 0.78);
instance uniform vec3 sun_glow_colour : source_color = vec3(1.0, 0.62, 0.3);
instance uniform float sun_glow_size = 4.5;
instance uniform float sun_streak_length = 0.16;
instance uniform float sun_strength = 1.0;

varying vec3 local_dir;

uint hash_bits(uvec3 q) {
	q *= uvec3(1597334673u, 3812015801u, 2798796415u);
	return (q.x ^ q.y ^ q.z) * 1597334673u;
}

float hash_cell(uvec3 q) {
	return float(hash_bits(q)) * (1.0 / 4294967295.0);
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

// One layer of stars: a point in each cell that has one, its light by how near
// the sight line passes.
vec3 stars(vec3 dir, float density, float share, float size, float seed) {
	vec3 p = dir * density;
	uvec3 cell = uvec3(ivec3(floor(p)) + 4096);
	float roll = hash_cell(cell + uvec3(uint(seed), 0u, 0u));
	if (roll > share) {
		return vec3(0.0);
	}
	vec3 at = vec3(
		hash_cell(cell + uvec3(11u + uint(seed), 3u, 5u)),
		hash_cell(cell + uvec3(7u, 13u + uint(seed), 17u)),
		hash_cell(cell + uvec3(19u, 23u, 29u + uint(seed)))) * 0.6 + 0.2;
	float d = length(fract(p) - at);
	float pixel = length(fwidth(p));
	float radius = pixel * size * (0.7 + (roll / share) * 1.3);
	float star = smoothstep(radius, 0.0, d);
	float tint = hash_cell(cell + uvec3(31u, 37u, 41u));
	float flicker = 1.0 - (twinkle * (0.5 + (0.5 * sin((TIME * (0.6 + tint * 2.0)) + (tint * 60.0)))));
	return mix(warm, cold, tint) * star * flicker;
}

// The sphere only has to enclose the camera. Each vertex is moved to the far
// plane along the same sight line, so the sky is never cut by the camera's far
// distance and the planet and its air are always in front of it.
void vertex() {
	vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	local_dir = normalize(world - CAMERA_POSITION_WORLD);
	vec4 clip = PROJECTION_MATRIX * (VIEW_MATRIX * vec4(CAMERA_POSITION_WORLD + (local_dir * 1000.0), 1.0));
	POSITION = vec4(clip.xy, clip.w * 0.000001, clip.w);
}

void fragment() {
	vec3 colour = stars(local_dir, faint_density, faint_share, 0.8, 0.0) * 0.7;
	colour += stars(local_dir, bright_density, bright_share, 1.3, 3.0) * 1.4;

	// The galaxy's haze: strongest along a great circle, broken up by noise.
	float band = 1.0 - abs(dot(local_dir, normalize(haze_axis)));
	float cloudy = value_noise(local_dir * 5.0) * 0.6 + value_noise(local_dir * 13.0) * 0.4;
	colour += haze_colour * haze_strength * pow(band, 6.0) * cloudy * 2.0;

	// The sun.
	vec3 sun = normalize(sun_direction);
	float away = degrees(acos(clamp(dot(local_dir, sun), -1.0, 1.0)));
	vec3 across = normalize(cross(sun, vec3(0.0, 1.0, 0.0)));
	vec3 up_axis = cross(sun, across);
	float sx = dot(local_dir, across);
	float sy = dot(local_dir, up_axis);
	float facing = step(0.0, dot(local_dir, sun));
	float glow = exp(-away / sun_glow_size) * 0.5;
	float rings = (step(away, sun_radius * 2.4) * 0.35) + (step(away, sun_radius * 4.5) * 0.2);
	float streaks = ((exp(-abs(sy) / 0.0025) * exp(-abs(sx) / sun_streak_length)) + (exp(-abs(sx) / 0.0025) * exp(-abs(sy) / sun_streak_length))) * 0.8;
	vec3 sun_light = (sun_glow_colour * (glow + rings * 0.5)) + (sun_colour * streaks);
	sun_light = mix(sun_light, sun_colour * 4.0, step(away, sun_radius));
	colour += sun_light * facing * sun_strength;

	ALBEDO = colour * brightness;
}
