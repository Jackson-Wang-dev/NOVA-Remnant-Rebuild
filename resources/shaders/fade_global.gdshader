shader_type spatial;
render_mode unshaded, cull_disabled, blend_mix;

// Ports Nova1's FadeGlobal.shaderproto - fragment logic is identical to ChangeTextureWithFade
// (Nova1 keeps them as separate named shader assets even though the math is the same, so vfx() calls
// naming either keep working independently). Sprite-only (Premul variant only in Nova1 - no PP).
uniform float _T : hint_range(0.0, 1.0) = 0.0;
uniform vec4 _Offsets = vec4(0.0, 0.0, 0.0, 0.0);
uniform vec4 _Color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform vec4 _SubColor : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform sampler2D _PrimaryTex : source_color, filter_linear;
uniform sampler2D _SubTex : source_color, filter_linear;

void fragment() {
	vec4 col1 = texture(_PrimaryTex, clamp(UV - _Offsets.xy, 0.0, 1.0)) * _Color;
	vec4 col2 = texture(_SubTex, clamp(UV - _Offsets.zw, 0.0, 1.0)) * _SubColor;
	vec4 col = mix(col1, col2, _T);
	ALBEDO = col.rgb;
	ALPHA = col.a;
}
