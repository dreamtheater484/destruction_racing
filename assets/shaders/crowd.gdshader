// The Bowl's crowd (plan §8.5): one low-poly seated spectator mesh, instanced by a MultiMesh, animated in the
// vertex shader. Vertex COLOR.r tags the body part (0 legs, 0.25 torso, 0.5 head, 0.75 left arm, 1 right arm).
// INSTANCE_CUSTOM: x phase, y temperament (how easily this person gets excited), z shirt colour, w skin/hair.
// `excitement` (0..1) comes from the game: idle fidgeting -> clapping -> standing, arms up and jumping.
shader_type spatial;
render_mode cull_back;

uniform float excitement = 0.15;
uniform vec3 action_point = vec3(0.0);
uniform float time_offset = 0.0;

varying vec3 v_albedo;

vec3 hsv2rgb(vec3 c) {
	vec3 p = abs(fract(c.xxx + vec3(1.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0);
	return c.z * mix(vec3(1.0), clamp(p - 1.0, 0.0, 1.0), c.y);
}

float h11(float x) { return fract(sin(x * 127.1) * 43758.5453); }

vec3 rot_x(vec3 v, vec3 pivot, float a) {
	vec3 d = v - pivot;
	float c = cos(a), s = sin(a);
	return pivot + vec3(d.x, d.y * c - d.z * s, d.y * s + d.z * c);
}

vec3 rot_y(vec3 v, vec3 pivot, float a) {
	vec3 d = v - pivot;
	float c = cos(a), s = sin(a);
	return pivot + vec3(d.x * c + d.z * s, d.y, -d.x * s + d.z * c);
}

vec3 rot_z(vec3 v, vec3 pivot, float a) {
	vec3 d = v - pivot;
	float c = cos(a), s = sin(a);
	return pivot + vec3(d.x * c - d.y * s, d.x * s + d.y * c, d.z);
}

void vertex() {
	float part = COLOR.r;
	vec4 ic = INSTANCE_CUSTOM;
	float t = TIME + time_offset;
	float ph = ic.x * 6.2831;
	float me = clamp(excitement * (0.55 + ic.y) + (ic.y - 0.5) * 0.15, 0.0, 1.0);
	float stand = smoothstep(0.55, 0.85, me + 0.08 * sin(t * 0.7 + ph));
	float clap = smoothstep(0.2, 0.45, me) * (1.0 - stand);
	float jump = stand * max(0.0, sin(t * 6.5 + ph)) * 0.07 * smoothstep(0.8, 1.0, me);

	vec3 rest = VERTEX;
	vec3 v = VERTEX;
	vec3 n = NORMAL;
	bool is_arm = part > 0.6;
	if (is_arm) {
		float side = part > 0.9 ? 1.0 : -1.0;
		vec3 shoulder = vec3(side * 0.205, 0.6, 0.0);
		// Resting on the knees -> clapping in front of the chest -> both arms up, waving.
		float rest_a = 0.55;
		float clap_a = 1.25 + 0.12 * sin(t * 11.0 + ph);
		float up_a = 2.75 + 0.22 * sin(t * 8.0 + ph + side);
		float a = mix(mix(rest_a, clap_a, clap), up_a, stand);
		float inward = clap * 0.38 * (0.8 + 0.2 * sin(t * 11.0 + ph)) - stand * 0.25;
		v = rot_z(v, shoulder, -side * inward);
		v = rot_x(v, shoulder, a);
		n = rot_z(n, vec3(0.0), -side * inward);
		n = rot_x(n, vec3(0.0), a);
	}
	// Heads and shoulders follow the action (yaw only, limited).
	vec3 world_origin = MODEL_MATRIX[3].xyz;
	vec3 fwd = -normalize(MODEL_MATRIX[2].xyz);
	vec3 to_action = action_point - world_origin;
	float yaw = atan(fwd.x * to_action.z - fwd.z * to_action.x, dot(fwd.xz, to_action.xz));
	yaw = clamp(-yaw, -1.1, 1.1) + 0.12 * sin(t * 0.37 + ph * 3.0);
	if (part > 0.4 && part < 0.6) {
		v = rot_y(v, vec3(0.0, 0.7, 0.0), yaw);
		n = rot_y(n, vec3(0.0), yaw);
	} else if (part > 0.2) {
		v = rot_y(v, vec3(0.0), yaw * 0.35);
		n = rot_y(n, vec3(0.0), yaw * 0.35);
	}
	// Idle fidget, standing up and jumping (the upper body rises; legs stay folded under the bench in front).
	if (part > 0.2) {
		v.y += stand * 0.36 + jump + 0.012 * sin(t * 1.3 + ph);
		v.z += stand * 0.05;
	}
	VERTEX = v;
	NORMAL = n;

	// Colours: a bright shirt from the spread of the stands, denim/black/khaki trousers, skin and hair.
	vec3 shirt = hsv2rgb(vec3(ic.z, 0.2 + 0.45 * h11(ic.z * 9.0), 0.18 + 0.5 * h11(ic.z * 3.0 + 1.0)));
	if (h11(ic.z * 17.0) > 0.82) shirt = vec3(0.85, 0.85, 0.82) * (0.7 + 0.3 * h11(ic.z * 5.0));
	float tsel = h11(ic.x * 31.0);
	vec3 trousers = tsel < 0.5 ? vec3(0.10, 0.14, 0.24) : tsel < 0.8 ? vec3(0.04, 0.04, 0.045) : vec3(0.36, 0.30, 0.20);
	vec3 skin = mix(vec3(0.24, 0.13, 0.08), vec3(0.78, 0.58, 0.45), fract(ic.w * 7.0));
	vec3 hair = mix(vec3(0.02, 0.015, 0.01), vec3(0.45, 0.32, 0.16), pow(fract(ic.w * 13.0), 2.0));
	if (part < 0.1) v_albedo = trousers;
	else if (part < 0.4) v_albedo = shirt;
	else if (part < 0.6) v_albedo = rest.y > 0.8 || rest.z > 0.04 ? hair : skin;
	else v_albedo = COLOR.g > 0.5 ? skin : shirt;
}

void fragment() {
	ALBEDO = v_albedo;
	ROUGHNESS = 0.85;
	SPECULAR = 0.2;
}
