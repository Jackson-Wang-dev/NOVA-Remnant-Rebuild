shader_type canvas_item;

#include "res://resources/shaders/blur.gdshaderinc"

// Ports Nova1's Glow.shaderproto. cam-only post-process layer.
uniform float _T : hint_range(0.0, 1.0) = 0.0;
uniform float _Size = 1.0;
uniform float _Strength = 1.0;
uniform sampler2D _MainTex : hint_screen_texture, repeat_disable, filter_linear;

void fragment() {
	vec4 col = texture(_MainTex, SCREEN_UV);

	vec3 glow = nova_tex2DGaussianBlur(_MainTex, SCREEN_PIXEL_SIZE, SCREEN_UV, _Size * _T).rgb;
	glow *= glow;
	glow *= glow;
	glow *= _Strength * _T;
	glow = clamp(glow, 0.0, 1.0);

	col.rgb = col.rgb + glow - col.rgb * glow;
	COLOR = col;
	COLOR.a = 1.0;
}
