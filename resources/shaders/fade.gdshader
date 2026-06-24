shader_type spatial;
render_mode unshaded, cull_disabled, blend_mix;

// Ports Nova1's Fade.shaderproto for single-object (Sprite3D) usage - test_fade.txt's
// vfx(bg, 'fade', t, { _SubTex=..., _Mask=... }) wipe/crossfade. _MainTex is explicitly copied from
// the Sprite3D's own Texture by VfxManager.GetState: unlike canvas_item nodes, a spatial ShaderMaterial
// has no automatic "this node's texture" builtin, so it can't be read implicitly here.
uniform float _T : hint_range(0.0, 1.0) = 0.0;
uniform float _Vague : hint_range(0.0, 0.5) = 0.25;
uniform float _Offset = 0.0;
uniform float _InvertMask : hint_range(0.0, 1.0) = 0.0;
uniform sampler2D _MainTex : source_color, filter_linear;
uniform sampler2D _SubTex : source_color, filter_linear;
uniform vec4 _SubColor : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform sampler2D _Mask : source_color, filter_linear;

void fragment() {
	float t0 = texture(_Mask, UV).r;
	t0 = _InvertMask + t0 - 2.0 * _InvertMask * t0;
	t0 = t0 * (1.0 - 2.0 * _Vague) + _Vague;
	float slope = 0.5 / (_Vague + 0.001);
	float mask = smoothstep(0.0, 1.0, 0.5 + slope * (_T - t0));

	vec4 col = texture(_MainTex, UV);
	col.rgb += _Offset * _T;
	col.rgb = clamp(col.rgb, 0.0, 1.0);
	vec4 col2 = texture(_SubTex, UV) * _SubColor;
	col = mix(col, col2, mask);

	ALBEDO = col.rgb;
	ALPHA = col.a;
}
