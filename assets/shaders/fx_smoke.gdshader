// Smoke, steam and dust puffs: camera-facing billboards with a billowing mask (baked noise), lit by the scene per
// vertex with wrap lighting, soft where they meet geometry. COLOR from the particle ramp sets tint and opacity;
// INSTANCE_CUSTOM.x (the particle angle) rotates and seeds each puff, .y is its age (0..1). Late in a derby the
// plumes are many layers deep at 4K: every pixel counts (M8: 4.8 ms of GPU down to 2.0).
shader_type spatial;
render_mode cull_disabled, depth_draw_never, shadows_disabled, diffuse_lambert_wrap, specular_disabled, vertex_lighting;

uniform sampler2D depth_tex : hint_depth_texture, filter_linear;
uniform float softness = 0.6;           // metres over which a puff fades into geometry
uniform float billow = 1.0;             // 0 = soft round puffs (steam), 1 = lumpy smoke
uniform float self_shadow = 0.45;       // darker cores, lighter rims

varying float seed;
varying float age;

// The billows: 4 octaves of value noise, baked once into a tiling texture 8 cells across (FxPool.SmokeNoise).
// Computed here it was 16 sin hashes a pixel of every layer of every plume, the most expensive pass of a late derby.
uniform sampler2D noise_tex : hint_default_white, filter_linear_mipmap, repeat_enable;

void vertex() {
	float ang = INSTANCE_CUSTOM.x;
	seed = fract(ang * 0.159155 + 0.5) * 37.0;
	age = INSTANCE_CUSTOM.y;
	mat4 world = mat4(normalize(INV_VIEW_MATRIX[0]), normalize(INV_VIEW_MATRIX[1]), normalize(INV_VIEW_MATRIX[2]), MODEL_MATRIX[3]);
	world = world * mat4(vec4(cos(ang), -sin(ang), 0.0, 0.0), vec4(sin(ang), cos(ang), 0.0, 0.0), vec4(0.0, 0.0, 1.0, 0.0), vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_MATRIX = VIEW_MATRIX * world;
	MODELVIEW_MATRIX = MODELVIEW_MATRIX * mat4(vec4(length(MODEL_MATRIX[0].xyz), 0.0, 0.0, 0.0), vec4(0.0, length(MODEL_MATRIX[1].xyz), 0.0, 0.0), vec4(0.0, 0.0, length(MODEL_MATRIX[2].xyz), 0.0), vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
	// Lit per vertex (four corners a puff: the lights' loop runs 4 times, not once a pixel of every layer); the
	// corners lean out, so a puff still reads round.
	NORMAL = normalize(vec3((UV * 2.0 - 1.0) * 0.6, 1.0));
}

void fragment() {
	vec2 c = UV * 2.0 - 1.0;
	float r = length(c);
	if (r > 1.23) discard;                  // past the reach of the noisy edge: nothing to blend
	float n = texture(noise_tex, (UV * 2.6 + vec2(seed, seed * 0.7) + vec2(0.0, -age * 0.8)) / 8.0).r;
	float edge = 1.0 - smoothstep(0.2, 1.0, r + (n - 0.5) * 0.45 * billow);
	float mask = edge * edge * mix(1.0, smoothstep(0.2, 0.65, n), 0.45 * billow);
	// Soft particles: fade where the puff meets the ground or a car.
	float scene_depth = texture(depth_tex, SCREEN_UV).r;
	vec4 sp = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, scene_depth, 1.0);
	float scene_z = -sp.z / sp.w;
	float fade = clamp((scene_z + VERTEX.z) / softness, 0.0, 1.0);
	// Billows: the lumps of the noise read as light and shade (never a dark core, which looks like a ring).
	ALBEDO = COLOR.rgb * (1.0 - self_shadow * 0.5 + self_shadow * (n - 0.5));
	float alpha = clamp(mask * COLOR.a * fade, 0.0, 1.0);
	if (alpha < 0.002) discard;             // the empty fringe (most of the corners): no blend
	ALPHA = alpha;
	ROUGHNESS = 1.0;
}
