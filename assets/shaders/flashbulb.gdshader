// Camera flashes in the night crowd (plan §8.5): camera-facing additive sprites that each pop for ~60 ms at
// random moments; the rate climbs with the crowd's excitement. They light nothing (no light, no fog energy).
// INSTANCE_CUSTOM: x phase, y personal rate.
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

uniform float excitement = 0.2;
uniform float energy = 18.0;

varying float v_flash;

float h11(float x) { return fract(sin(x * 91.7) * 43758.5453); }

void vertex() {
	// Billboard: keep the instance position, face the camera.
	vec3 origin = MODEL_MATRIX[3].xyz;
	vec3 right = normalize(INV_VIEW_MATRIX[0].xyz);
	vec3 up = normalize(INV_VIEW_MATRIX[1].xyz);
	vec3 world = origin + right * VERTEX.x + up * VERTEX.y;
	POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(world, 1.0);
	// Slots of 0.5 s; a slot flashes with a probability that rises with excitement.
	float rate = 0.4 + INSTANCE_CUSTOM.y;
	float t = TIME * rate * 2.0 + INSTANCE_CUSTOM.x * 97.0;
	float slot = floor(t);
	float chance = 0.002 + 0.05 * excitement * excitement;
	float on = step(1.0 - chance, h11(slot + INSTANCE_CUSTOM.x * 13.0));
	float f = fract(t);
	v_flash = on * (1.0 - smoothstep(0.0, 0.06 * rate * 2.0, f));
}

void fragment() {
	vec2 d = UV * 2.0 - 1.0;
	float r = length(d);
	float core = exp(-r * r * 18.0);
	float halo = exp(-r * r * 3.0) * 0.25;
	ALBEDO = vec3(0.92, 0.95, 1.0) * (core + halo) * v_flash * energy;
	if (v_flash <= 0.0) discard;
}
