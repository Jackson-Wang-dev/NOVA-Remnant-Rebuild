shader_type canvas_item;

#include "res://resources/shaders/noise.gdshaderinc"

// Ports Nova1's Colorless.shaderproto ("失色症" disease reveal in ch4.txt/rain_diary.txt - always
// stacked as a layer alongside mono/color, see VfxManager.cs). A noise-driven R/G channel split,
// masked by luminance^4 so bright areas distort more than dark ones. cam-only.
uniform float _T : hint_range(0.0, 1.0) = 0.0;
uniform sampler2D _MainTex : hint_screen_texture, repeat_disable, filter_linear;

void fragment() {
	vec4 col = texture(_MainTex, SCREEN_UV);

	vec2 v = 0.001 * nova_snoise2(vec3(SCREEN_UV * 10.0, TIME));
	float gray = dot(col.rgb, vec3(0.299, 0.587, 0.114));
	float mask = gray * gray;
	mask = 2.0 * mask * mask * _T;
	v *= mask;

	col.r = texture(_MainTex, SCREEN_UV + v).r;
	col.g = texture(_MainTex, SCREEN_UV - v).g;

	COLOR = col;
	// Full-screen post layer - always fully opaque, see mono.gdshader for why.
	COLOR.a = 1.0;
}
