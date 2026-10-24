// A nest's hanging pickles further off (PodField): a few pixels each, and
// thousands of them, lit per vertex with no shadow lookup, as the horde's far
// pickles are. pickle_hang.gdshaderinc.
shader_type spatial;
render_mode diffuse_toon, specular_disabled, world_vertex_coords, vertex_lighting, shadows_disabled;

#include "res://shaders/pickle_hang.gdshaderinc"
