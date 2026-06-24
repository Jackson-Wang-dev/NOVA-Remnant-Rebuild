shader_type canvas_item;

// cam/screen variant of fade.gdshader (see that file - this is the same Fade.shaderproto port, just
// for the VfxLayer post-process path instead of a single Sprite3D). _MainTex has to be
// hint_screen_texture here, not an explicitly-assigned source_color texture like the sprite version:
// trans()/trans_fade()'s cam branch swaps the live scene (via the caller's callback) *during* this
// same script block, before anything has actually rendered a new frame, so there is no static "new
// frame" texture to capture and assign synchronously - hint_screen_texture instead auto-samples
// whatever the cam VfxLayer stack has drawn so far underneath this layer, which is already the
// post-swap scene by the time this layer's fragment shader actually runs.
uniform float _T : hint_range(0.0, 1.0) = 0.0;
uniform float _Vague : hint_range(0.0, 0.5) = 0.25;
uniform float _Offset = 0.0;
uniform float _InvertMask : hint_range(0.0, 1.0) = 0.0;
uniform sampler2D _MainTex : hint_screen_texture, repeat_disable, filter_linear;
uniform sampler2D _SubTex : source_color, filter_linear;
uniform vec4 _SubColor : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform sampler2D _Mask : source_color, filter_linear;

void fragment() {
	float t0 = texture(_Mask, UV).r;
	t0 = _InvertMask + t0 - 2.0 * _InvertMask * t0;
	t0 = t0 * (1.0 - 2.0 * _Vague) + _Vague;
	float slope = 0.5 / (_Vague + 0.001);
	float mask = smoothstep(0.0, 1.0, 0.5 + slope * (_T - t0));

	vec4 col = texture(_MainTex, SCREEN_UV);
	col.rgb += _Offset * _T;
	col.rgb = clamp(col.rgb, 0.0, 1.0);
	vec4 col2 = texture(_SubTex, UV) * _SubColor;
	col = mix(col, col2, mask);

	COLOR = col;
}
