// Soft ambient-occlusion blob under a car (multiplies whatever ground is below), darker at the tyres.
shader_type spatial;
render_mode blend_mul, unshaded, cull_disabled, depth_draw_never, shadows_disabled;

uniform vec2 half_size = vec2(0.85, 2.15);     // body footprint (m)
uniform float softness = 0.45;
uniform float strength : hint_range(0.0, 1.0) = 0.72;
uniform vec4 wheels[4];                        // xy = wheel contact centre (m, local), z = radius of the blob
uniform float wheel_strength : hint_range(0.0, 1.0) = 0.6;

varying vec2 local;

void vertex() {
	local = VERTEX.xz;
}

float sd_round_box(vec2 p, vec2 b, float r) {
	vec2 q = abs(p) - b + r;
	return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
}

void fragment() {
	float d = sd_round_box(local, half_size, 0.35);
	float body = 1.0 - smoothstep(-softness, softness, d);
	float tyres = 0.0;
	for (int i = 0; i < 4; i++) {
		vec2 c = wheels[i].xy;
		float r = max(wheels[i].z, 0.01);
		tyres = max(tyres, 1.0 - smoothstep(0.0, r, length((local - c) * vec2(1.6, 0.8))));
	}
	float occ = max(body * strength, tyres * wheel_strength);
	ALBEDO = vec3(1.0 - occ);
}
