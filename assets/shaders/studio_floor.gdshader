// Studio floor that fades into the background colour, so the stage reads as an endless cyclorama.
shader_type spatial;

uniform vec3 color_near : source_color = vec3(0.12);
uniform vec3 color_far : source_color = vec3(0.05);
uniform float fade_start = 5.0;
uniform float fade_end = 16.0;
uniform float roughness_value : hint_range(0.0, 1.0) = 0.32;

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	float t = smoothstep(fade_start, fade_end, length(world_pos.xz));
	ALBEDO = color_near * (1.0 - t);
	EMISSION = color_far * t;
	ROUGHNESS = roughness_value;
	SPECULAR = 0.5 * (1.0 - t);
}
