// Smoke, steam and dust puffs: camera-facing billboards with a procedural billowing mask (no texture), lit by
// the scene with wrap lighting, soft where they meet geometry. COLOR from the particle ramp sets tint and
// opacity; INSTANCE_CUSTOM.x (the particle angle) rotates and seeds each puff, .y is its age (0..1).
shader_type spatial;
render_mode cull_disabled, depth_draw_never, shadows_disabled, diffuse_lambert_wrap, specular_disabled;

uniform sampler2D depth_tex : hint_depth_texture, filter_linear;
uniform float softness = 0.6;           // metres over which a puff fades into geometry
uniform float billow = 1.0;             // 0 = soft round puffs (steam), 1 = lumpy smoke
uniform float self_shadow = 0.45;       // darker cores, lighter rims

varying float seed;
varying float age;

float fh(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float fnoise(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(fh(i), fh(i + vec2(1, 0)), f.x), mix(fh(i + vec2(0, 1)), fh(i + vec2(1, 1)), f.x), f.y);
}
float ffbm(vec2 p) {
	float s = 0.0, a = 0.5;
	for (int i = 0; i < 4; i++) { s += a * fnoise(p); p = p * 2.07 + 3.1; a *= 0.5; }
	return s;
}

void vertex() {
	float ang = INSTANCE_CUSTOM.x;
	seed = fract(ang * 0.159155 + 0.5) * 37.0;
	age = INSTANCE_CUSTOM.y;
	mat4 world = mat4(normalize(INV_VIEW_MATRIX[0]), normalize(INV_VIEW_MATRIX[1]), normalize(INV_VIEW_MATRIX[2]), MODEL_MATRIX[3]);
	world = world * mat4(vec4(cos(ang), -sin(ang), 0.0, 0.0), vec4(sin(ang), cos(ang), 0.0, 0.0), vec4(0.0, 0.0, 1.0, 0.0), vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_MATRIX = VIEW_MATRIX * world;
	MODELVIEW_MATRIX = MODELVIEW_MATRIX * mat4(vec4(length(MODEL_MATRIX[0].xyz), 0.0, 0.0, 0.0), vec4(0.0, length(MODEL_MATRIX[1].xyz), 0.0, 0.0), vec4(0.0, 0.0, length(MODEL_MATRIX[2].xyz), 0.0), vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
}

void fragment() {
	vec2 c = UV * 2.0 - 1.0;
	float r = length(c);
	float n = ffbm(UV * 2.6 + vec2(seed, seed * 0.7) + vec2(0.0, -age * 0.8));
	float edge = 1.0 - smoothstep(0.2, 1.0, r + (n - 0.5) * 0.45 * billow);
	float mask = edge * edge * mix(1.0, smoothstep(0.2, 0.65, n), 0.45 * billow);
	// Soft particles: fade where the puff meets the ground or a car.
	float scene_depth = texture(depth_tex, SCREEN_UV).r;
	vec4 sp = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, scene_depth, 1.0);
	float scene_z = -sp.z / sp.w;
	float fade = clamp((scene_z + VERTEX.z) / softness, 0.0, 1.0);
	// Billows: the lumps of the noise read as light and shade (never a dark core, which looks like a ring).
	ALBEDO = COLOR.rgb * (1.0 - self_shadow * 0.5 + self_shadow * (n - 0.5));
	ALPHA = clamp(mask * COLOR.a * fade, 0.0, 1.0);
	NORMAL = normalize(vec3(c * 0.6, 1.0));
	ROUGHNESS = 1.0;
}
