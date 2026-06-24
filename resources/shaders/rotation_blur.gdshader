shader_type canvas_item;

#include "res://resources/shaders/blur.gdshaderinc"

// Ports Nova1's RotationBlur.shaderproto. cam-only post-process layer.
uniform float _T : hint_range(0.0, 1.0) = 0.0;
uniform float _Size = 1.0;
uniform float _Offset = 0.0;
uniform sampler2D _MainTex : hint_screen_texture, repeat_disable, filter_linear;

void fragment() {
	vec2 uvShift = SCREEN_UV - 0.5;
	vec2 vel = vec2(uvShift.y, -uvShift.x) * _Size * _T;
	vec4 col = nova_tex2DMotionBlur(_MainTex, SCREEN_PIXEL_SIZE, SCREEN_UV, vel);
	col.rgb += _Offset * length(uvShift) * _T;
	col.rgb = clamp(col.rgb, 0.0, 1.0);
	COLOR = col;
	COLOR.a = 1.0;
}
