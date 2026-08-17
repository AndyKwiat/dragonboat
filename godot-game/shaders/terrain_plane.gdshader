shader_type spatial;
render_mode unshaded, cull_disabled;

// The map texture holds the config's sRGB hex colours as raw bytes (no
// colour-space hint, so it's sampled 1:1); we convert to linear ourselves so
// the on-screen colours match the hex values (and the minimap) exactly.
uniform sampler2D map : filter_nearest, repeat_disable;
uniform vec2 world_size;
uniform float tile_scale;
uniform vec3 grid_color : source_color;
uniform float grid_opacity;

varying vec2 world_pos;

vec3 srgb_to_linear(vec3 c) {
	return mix(pow((c + 0.055) / 1.055, vec3(2.4)), c / 12.92, lessThan(c, vec3(0.04045)));
}

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz;
}

void fragment() {
	// Mixed in the texture's sRGB space with the (linear) grid colour, exactly
	// like the web version's raw shader, then converted for output.
	vec3 terrain = texture(map, world_pos / world_size).rgb;
	vec2 tile = world_pos / tile_scale;
	vec2 g = abs(fract(tile - 0.5) - 0.5) / fwidth(tile);
	float line = 1.0 - min(min(g.x, g.y), 1.0);
	ALBEDO = srgb_to_linear(mix(terrain, grid_color, line * grid_opacity));
}
