shader_type spatial;
render_mode unshaded, cull_disabled;

// Vertex colours are the config's sRGB hex colours (sides pre-darkened);
// convert to linear so the output matches the hex values.
vec3 srgb_to_linear(vec3 c) {
	return mix(pow((c + 0.055) / 1.055, vec3(2.4)), c / 12.92, lessThan(c, vec3(0.04045)));
}

void fragment() {
	ALBEDO = srgb_to_linear(COLOR.rgb);
}
