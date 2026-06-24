shader_type canvas_item;

// Ports Nova1's MixAdd.shaderproto. cam-only post-process layer. Uses the Default-variant IADD_RGBA
// (simple add) per M8's "skip the 5-variant template system" decision - Nova1's PP variant would
// premultiply _ColorAdd's rgb by the base color's alpha first, but our full-screen post layers are
// always forced to alpha=1 (see mono.gdshader), so the two formulations are equivalent here anyway.
// _Mask is a static texture path with no Unity-style per-texture Tiling/Offset (_Mask_ST) support,
// matching the established simplification used by every other masked effect in this project.
uniform float _T : hint_range(0.0, 1.0) = 0.0;
uniform vec4 _ColorMul : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform vec4 _ColorAdd = vec4(0.0, 0.0, 0.0, 0.0);
uniform float _InvertMask : hint_range(0.0, 1.0) = 0.0;
uniform float _AlphaFactor = 0.0;
uniform sampler2D _MainTex : hint_screen_texture, repeat_disable, filter_linear;
uniform sampler2D _Mask : source_color, filter_linear;

void fragment() {
	vec4 col = texture(_MainTex, SCREEN_UV);
	float mask = texture(_Mask, SCREEN_UV).r;
	mask = _InvertMask + mask - 2.0 * _InvertMask * mask;

	vec4 maskColor = vec4(vec3(mask), col.a);
	maskColor *= _ColorMul;
	maskColor += _ColorAdd;
	maskColor.a *= _AlphaFactor;

	col = clamp(col + _T * maskColor, 0.0, 1.0);
	COLOR = col;
	COLOR.a = 1.0;
}
