shader_type canvas_item;

#include "res://resources/shaders/noise.gdshaderinc"

// Ports Nova1's Wiggle.shaderproto. cam-only post-process layer.
uniform float _T : hint_range(0.0, 1.0) = 0.0;
uniform float _Mul = 1.0;
uniform float _Offset = 0.0;
uniform float _XAmp = 0.0;
uniform float _YAmp = 0.0;
uniform float _AAmp = 0.0;
uniform float _BlinkAmp = 0.0;
uniform float _XFreq = 0.0;
uniform float _YFreq = 0.0;
uniform float _TFreq = 0.0;
uniform float _BlinkFreq = 0.0;
uniform float _Mono : hint_range(0.0, 1.0) = 0.0;
uniform sampler2D _MainTex : hint_screen_texture, repeat_disable, filter_linear;

void fragment() {
	vec3 xyt = vec3(_XFreq * SCREEN_UV.x, _YFreq * SCREEN_UV.y, _TFreq * TIME);
	vec2 deltaUV = vec2(_XAmp, _YAmp) * nova_snoise2(xyt) * _T;

	vec4 col = texture(_MainTex, SCREEN_UV + deltaUV);
	float gray = dot(col.rgb, vec3(0.299, 0.587, 0.114));
	col.rgb = mix(col.rgb, vec3(gray), _Mono);

	col.rgb *= _Mul;
	col.rgb += _Offset;
	float noiseBlink = nova_noise(_BlinkFreq * TIME);
	col.rgb += _BlinkAmp * noiseBlink * noiseBlink * _T;

	float noiseA = _AAmp * nova_noise(xyt);
	col.rgb *= 1.0 - noiseA;
	col.rgb = clamp(col.rgb, 0.0, 1.0);

	COLOR = col;
	COLOR.a = 1.0;
}
