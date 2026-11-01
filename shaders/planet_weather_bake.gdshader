// The weather of the menu planet drawn into a map, once a frame, for the cloud
// deck to read (CloudMaps draws it in a SubViewport and hands it to every shader
// as the global planet_cloud_flow). Each texel is a direction on the planet, in
// the octahedral layout of weather_fold(); its colour is where the cloud pattern
// there is read from, a unit vector packed into 0 to 1.
//
// CloudMaps copies the weather settings over from the cloud material, so they
// are tuned in one place.

shader_type canvas_item;
render_mode unshaded, blend_disabled;

#include "res://shaders/planet_weather.gdshaderinc"

void fragment() {
	// The texel's place on the folded square, the border texels just past it.
	float edge = 1.0 / SCREEN_PIXEL_SIZE.x;
	vec2 o = ((((UV * edge) - 1.0) / (edge - 2.0)) * 2.0) - 1.0;
	vec3 d = weather_unfold(o);
	COLOR = vec4((weather_flow(d) * 0.5) + 0.5, 1.0);
}
