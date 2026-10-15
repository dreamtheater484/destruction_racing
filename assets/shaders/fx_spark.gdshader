// Spark streak: a quad stretched along the particle's velocity (ParticleProcessMaterial with align_y), turned
// to face the camera around that axis. Additive, hot white-yellow cooling to orange (COLOR from the ramp).
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;

uniform float intensity = 14.0;   // HDR: hot enough to read in full sun and to bloom

void vertex() {
	vec3 axis_y = MODEL_MATRIX[1].xyz;
	vec3 pos = MODEL_MATRIX[3].xyz;
	vec3 to_cam = normalize(CAMERA_POSITION_WORLD - pos);
	vec3 side = cross(axis_y, to_cam);
	float sl = length(side);
	side = sl > 1e-5 ? side / sl : vec3(1.0, 0.0, 0.0);
	side *= length(MODEL_MATRIX[0].xyz);
	mat4 world = mat4(vec4(side, 0.0), vec4(axis_y, 0.0), vec4(to_cam, 0.0), vec4(pos, 1.0));
	MODELVIEW_MATRIX = VIEW_MATRIX * world;
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
}

void fragment() {
	// Bright core along the streak, soft across it, fading towards the tail.
	float across = smoothstep(0.0, 0.7, 1.0 - abs(UV.x * 2.0 - 1.0));
	float along = smoothstep(0.0, 0.2, UV.y) * (1.0 - smoothstep(0.8, 1.0, UV.y));
	float a = across * along * COLOR.a;
	ALBEDO = COLOR.rgb * intensity * a;
	ALPHA = a;
}
