// A headlight beam through the night haze and the smoke (plan §8.1): an additive cone mesh with soft edges and
// a depth fade where it meets the floor or a car, instead of volumetric fog (which ghosts behind moving lights).
// The cone is a CylinderMesh: narrow end (UV.y = 0) at the lamp, wide end at the far end of the beam.
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

uniform vec3 colour : source_color = vec3(1.0, 0.9, 0.74);
uniform float intensity = 0.1;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;

instance uniform float strength = 1.0;

varying float v_along;

void vertex() {
	v_along = UV.y;
}

void fragment() {
	float depth = textureLod(depth_tex, SCREEN_UV, 0.0).r;
	vec4 view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
	float scene_z = -view.z / view.w;
	float frag_z = -VERTEX.z;
	float soft = clamp((scene_z - frag_z) / 1.2, 0.0, 1.0);
	// Brightest along the beam's core (surfaces seen face-on), fading along its length and near the camera.
	float core = pow(abs(dot(normalize(NORMAL), normalize(VIEW))), 1.5);
	float along = (1.0 - v_along) * (1.0 - v_along) * smoothstep(0.0, 0.08, v_along);
	float near_fade = smoothstep(0.6, 3.0, frag_z);
	ALBEDO = colour * intensity * strength * core * along * soft * near_fade;
}
