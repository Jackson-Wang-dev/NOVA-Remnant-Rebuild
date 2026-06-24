shader_type canvas_item;

#include "res://resources/shaders/blur.gdshaderinc"

// Ports Nova1's Sharpen.shaderproto (unsharp mask: original + strength * (original - blurred)).
// cam-only post-process layer.
uniform float _T : hint_range(0.0, 1.0) = 0.0;
uniform float _Size = 1.0;
uniform float _Strength = 1.0;
uniform sampler2D _MainTex : hint_screen_texture, repeat_disable, filter_linear;

void fragment() {
	vec4 col = texture(_MainTex, SCREEN_UV);
	col += _Strength * _T * (col - nova_tex2DGaussianBlur(_MainTex, SCREEN_PIXEL_SIZE, SCREEN_UV, _Size * _T));
	COLOR = col;
	COLOR.a = 1.0;
}
