// Engine fire: camera-facing flame tongues with scrolling procedural noise (no flipbook needed).
// Hot yellow base, orange body, dark red tips; INSTANCE_CUSTOM.y is the particle's age (0..1).
// Premultiplied alpha: each tongue glows but also covers what is behind it a little, so a cluster of
// overlapping tongues stays orange instead of adding up to white.
shader_type spatial;
render_mode unshaded, blend_premul_alpha, cull_disabled, depth_draw_never, shadows_disabled;

uniform float intensity = 1.7;
uniform float cover = 0.55;          // how much a tongue hides what is behind it

varying float seed;
varying float age;

float fh(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float fnoise(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(fh(i), fh(i + vec2(1, 0)), f.x), mix(fh(i + vec2(0, 1)), fh(i + vec2(1, 1)), f.x), f.y);
}

void vertex() {
	seed = fract(INSTANCE_CUSTOM.x * 0.159155 + 0.5) * 53.0;
	age = INSTANCE_CUSTOM.y;
	// Upright billboard: flames rise, so keep world up and only turn to face the camera.
	vec3 pos = MODEL_MATRIX[3].xyz;
	vec3 to_cam = CAMERA_POSITION_WORLD - pos;
	to_cam.y = 0.0;
	to_cam = length(to_cam) > 1e-4 ? normalize(to_cam) : vec3(0.0, 0.0, 1.0);
	vec3 up = vec3(0.0, 1.0, 0.0);
	vec3 side = normalize(cross(up, to_cam));
	float s = length(MODEL_MATRIX[0].xyz);
	mat4 world = mat4(vec4(side * s, 0.0), vec4(up * s * 1.6, 0.0), vec4(to_cam * s, 0.0), vec4(pos, 1.0));
	MODELVIEW_MATRIX = VIEW_MATRIX * world;
}

void fragment() {
	vec2 uv = UV;
	float t = TIME * 2.2 + seed;
	float n = fnoise(vec2(uv.x * 4.0 + seed, uv.y * 3.0 + t)) * 0.65 + fnoise(vec2(uv.x * 9.0, uv.y * 7.0 + t * 1.7)) * 0.35;
	// Tongue shape: wide at the base, licking up to a point; the noise eats into it.
	float x = (uv.x - 0.5) * 2.0;
	float h = 1.0 - uv.y;                         // 0 at the bottom of the quad, 1 at the top
	float width = mix(0.68, 0.05, pow(h, 0.7));
	float lick = (n - 0.5) * 0.55 * h;            // the tip wanders sideways
	float shape = 1.0 - smoothstep(width * 0.35, width, abs(x + lick) + (n - 0.5) * 0.3);
	shape *= (1.0 - smoothstep(0.45, 1.0, h + (n - 0.5) * 0.5)) * smoothstep(0.0, 0.15, h);
	shape *= (1.0 - smoothstep(0.78, 0.98, abs(x))) * (1.0 - smoothstep(0.85, 1.0, h));    // never reaches the quad's edges
	float heat = clamp(shape * (1.25 - h) * (1.0 - age * 0.75), 0.0, 1.0);
	vec3 col = mix(vec3(0.55, 0.07, 0.01), vec3(1.0, 0.40, 0.05), smoothstep(0.08, 0.45, heat));
	col = mix(col, vec3(1.0, 0.78, 0.38), smoothstep(0.7, 1.0, heat));
	float a = shape * COLOR.a;
	ALBEDO = col * intensity * a;
	ALPHA = a * cover;
}
