shader_type canvas_item;

#include "res://resources/shaders/noise.gdshaderinc"

// Ports Nova1's RandRoll.shaderproto. cam-only post-process layer. _T is declared but unused in the
// fragment math, matching the original (Nova1's own shaderproto never references _T either) - kept
// as a uniform anyway so vfx()'s shader_parameter/_T write doesn't target a nonexistent parameter.
uniform float _T : hint_range(0.0, 1.0) = 0.0;
uniform float _Freq = 10.0;
uniform sampler2D _MainTex : hint_screen_texture, repeat_disable, filter_linear;

void fragment() {
	vec2 uv = SCREEN_UV + nova_rand2(floor(_Freq * TIME));
	COLOR = texture(_MainTex, fract(uv));
	COLOR.a = 1.0;
}
