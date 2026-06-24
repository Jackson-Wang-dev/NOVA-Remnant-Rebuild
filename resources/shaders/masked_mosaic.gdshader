shader_type canvas_item;

#include "res://resources/shaders/noise.gdshaderinc"

// Ports Nova1's MaskedMosaic.shaderproto. cam-only post-process layer. Unity's i.vertex (SV_POSITION,
// raw screen pixel coords) -> Godot's FRAGCOORD.xy (also raw window pixel coords) for the per-block
// hash seed. _Mask is a static texture path per M8 scope decision (same pattern as fade.gdshader).
uniform float _T : hint_range(0.0, 1.0) = 0.0;
uniform float _Size = 4.0;
uniform float _Strength = 8.0;
uniform sampler2D _MainTex : hint_screen_texture, repeat_disable, filter_linear;
uniform sampler2D _Mask : source_color, filter_linear;

void fragment() {
	vec4 col = texture(_MainTex, SCREEN_UV);

	vec2 blockPos = floor(FRAGCOORD.xy / _Size) * _Size;
	vec2 delta = nova_rand2(vec3(blockPos, TIME)) * 2.0 - 1.0;
	vec2 uv2 = SCREEN_UV + delta * SCREEN_PIXEL_SIZE * _Strength;
	vec4 col2 = texture(_MainTex, uv2);

	float mask = texture(_Mask, SCREEN_UV).r * _T;
	COLOR = mix(col, col2, mask);
	COLOR.a = 1.0;
}
