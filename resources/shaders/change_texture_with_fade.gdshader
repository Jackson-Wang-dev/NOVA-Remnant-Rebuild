shader_type spatial;
render_mode unshaded, cull_disabled, blend_mix;

// Ports Nova1's ChangeTextureWithFade.shaderproto. Sprite-only (Nova1 only ever generates the Default
// variant for this one, no PP variant - i.e. it was never meant for cam), used on bg/fg/Sprite3D
// targets. _MainTex in the original is a dummy "size provider" never actually read in the fragment
// shader - the real textures are _PrimaryTex/_SubTex, each with its own independent UV offset. We
// don't declare _MainTex at all since nothing here uses it (VfxManager's generic node-texture
// forwarding to "_MainTex" is a silent no-op for a shader that has no such parameter).
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
