// The char a held beam leaves behind it (ScorchTrail): one quad per mark,
// drawn from one multimesh. Ragged black edges from world-space noise, so
// marks laid end to end join into one ribbon; an ember core that glows and
// cools; and, as it ages, a dissolve from the edges in until nothing is left.
// Per instance, INSTANCE_CUSTOM: x = time laid (seconds, the trail's clock),
// y = a seed.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_opaque, shadows_disabled;

uniform float now = 0.0;
uniform float lifetime = 20.0;
uniform float ember_seconds = 2.5;
uniform vec3 char_dark : source_color = vec3(0.05, 0.04, 0.035);
uniform vec3 char_light : source_color = vec3(0.16, 0.13, 0.1);
uniform vec3 ember_colour : source_color = vec3(1.0, 0.38, 0.06);
uniform float ember_energy = 4.0;

varying float v_born;
varying vec3 v_world;

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x),
		mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

void vertex() {
	v_born = INSTANCE_CUSTOM.x;
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	float age = max(now - v_born, 0.0);
	float life = clamp(age / lifetime, 0.0, 1.0);
	// World-space, so the next mark along carries on the same pattern.
	vec2 w = v_world.xz + v_world.y * 0.37;
	float n = noise(w * 1.3) * 0.65 + noise(w * 4.1) * 0.35;

	vec2 p = UV * 2.0 - 1.0;
	float edge = abs(p.x) + (n - 0.5) * 0.7;
	if (edge > 1.0 - life * 1.6) {
		discard;
	}

	ALBEDO = mix(char_dark, char_light, n * n);
	float ember = (1.0 - smoothstep(0.0, ember_seconds, age)) * clamp(1.0 - edge * 1.6, 0.0, 1.0);
	ALBEDO = mix(ALBEDO, ember_colour, ember * 0.6);
	EMISSION = ember_colour * ember * ember_energy * (0.6 + n * 0.8);
}
