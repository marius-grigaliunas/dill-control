// A nest's hanging pickles (PodField): every full pod's pickle, drawn by
// instancing in world space, hanging from its pod by the top. Included by
// pickle_hang (near: capsules, lit per pixel) and pickle_hang_far (columns,
// lit per vertex and shadowless, as the horde's far pickles are).
//
// - Each instance's custom data .r is its vine's row of flex_rods: it bends
//   with its vine as the vine's voxels and leaves do (flex_bend.gdshaderinc).
//   flex_row is 0 to bend, -1 (FlexField.BenchStill) to draw them as built.
// - Each swings a little about where it hangs, out of step with the next.
// Toon lit, like the horde's pickles; the ink pass outlines them.
#include "res://shaders/flex_bend.gdshaderinc"

uniform vec4 colour : source_color = vec4(0.36, 0.62, 0.18, 1.0);
// Half the pickle's height: the instance stands at its middle, it hangs from its top.
uniform float half_height = 0.9;
// How far its bottom swings to each side, and how often.
uniform float sway_m = 0.15;
uniform float sway_hz = 0.35;

void vertex() {
	vec3 up = normalize(MODEL_MATRIX[1].xyz);
	vec3 side = normalize(MODEL_MATRIX[0].xyz);
	vec3 hang = MODEL_MATRIX[3].xyz + up * half_height;
	float below = clamp(dot(hang - VERTEX, up) / (2.0 * half_height), 0.0, 1.0);
	// From where it hangs, not its slot: a rebuilt buffer moves no pickle.
	float phase = dot(MODEL_MATRIX[3].xyz, vec3(1.37, 1.71, 1.53));
	VERTEX += side * (sin(TIME * sway_hz * TAU + phase) * sway_m * below);
	if (flex_row >= 0) {
		VERTEX = flex_bend_row(VERTEX, int(INSTANCE_CUSTOM.r + 0.5));
	}
}

void fragment() {
	ALBEDO = colour.rgb;
	ROUGHNESS = 1.0;
}
