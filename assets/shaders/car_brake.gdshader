// The brakes behind the spokes (src/Vehicles/BrakeMesh.cs): vertex colour = albedo, its alpha = metalness;
// brake dust darkens and browns them as an event goes on (CarVisual).
shader_type spatial;

uniform float dust : hint_range(0.0, 1.0) = 0.0;
uniform vec3 dust_color : source_color = vec3(0.12, 0.095, 0.075);

void fragment() {
	float metal = COLOR.a;
	ALBEDO = mix(COLOR.rgb, dust_color, dust * 0.6);
	METALLIC = metal * (1.0 - dust * 0.5);
	ROUGHNESS = mix(0.62, 0.38, metal) + dust * 0.2;
}
