shader_type canvas_item;

#include "res://resources/shaders/noise.gdshaderinc"
#include "res://resources/shaders/blur.gdshaderinc"

// Ports Nova1's Rain.shaderproto (rain-on-glass refraction, classic shadertoy-style layered drop
// simulation), with three uniforms added beyond the original (_Density/_Angle/_Speed) per explicit
// user request - Nova1 only ever exposed _T/_Size/_Aspect and hardcoded both the flow direction
// (always +Y in UV space) and the drop density (fixed grid constants).
//
// Direction convention: _Angle is degrees, 0 = falls straight down on screen; increasing _Angle sweeps
// counterclockwise as drawn on screen (90 = right, 180 = up, 270 = left, so e.g. 225 - between 180/up
// and 270/left - points up-and-left). The original's hardcoded "+t" scroll, ported as-is, actually
// flowed *upward* on screen in Godot - Unity's effective UV-space for this shader read top-to-bottom
// opposite of Godot's top-left-origin SCREEN_UV, so a literal port inherited the wrong default
// direction. Fixed entirely via nova_rain_rotate_to_flow (a rigid 2D rotation, baked-in 180° baseline
// + _Angle) - nova_rain_drop_layer's own "+t" math is untouched from the original. A first attempt
// instead flipped the sign of "t" inside drop_layer directly, which broke the trail/drop relationship
// (trailFront's "st.y - y" sign assumes a specific direction convention baked into the original math) -
// that produced drops visibly moving toward their own trail, and trails for the two layers (m1/m2,
// which still shared the same flipped sign) ending up looking like crossing opposite-direction streaks.
// A rigid rotation reorients the whole pattern without touching any of those internal relationships.
uniform float _T : hint_range(0.0, 1.0) = 0.0;
uniform float _Size = 2.0;
uniform float _Aspect = 1.77777778;
uniform float _Density : hint_range(0.1, 5.0) = 1.0;
uniform float _Angle : hint_range(0.0, 360.0) = 0.0;
uniform float _Speed = 1.0;
uniform sampler2D _MainTex : hint_screen_texture, repeat_disable, filter_linear;

float nova_rain_s(float a, float b, float t) {
	t = clamp((t - a) / (b - a), 0.0, 1.0);
	return t * t * (3.0 - 2.0 * t);
}

float nova_rain_saw(float b, float t) {
	return nova_rain_s(0.0, b, t) * nova_rain_s(1.0, b, t);
}

float nova_rain_static_drops(vec2 uv, float t) {
	vec3 n = nova_rand3(floor(uv));
	float d = length(fract(uv) - n.xy);
	float fade = nova_rain_saw(0.5, fract(t + n.z));
	float c = nova_rain_s(0.3, 0.0, d) * fract(n.z * 1000.0) * fade;
	return c;
}

const float NOVA_PI = 3.1415927;

// Rigidly rotates uv by (angleRad + 180°) - the +180° baseline is what makes _Angle=0 read as "down"
// on screen (see the up/down direction note above); _Angle then rotates that baseline clockwise.
// nova_rain_drop_layer's own "+t" logic, and every relationship derived from it (trail position,
// wiggle, droplets), is completely untouched - only the input coordinates are reoriented as a unit.
vec2 nova_rain_rotate_to_flow(vec2 uv, float angleRad) {
	float a = angleRad + NOVA_PI;
	float s = sin(a);
	float c = cos(a);
	return vec2(uv.x * c - uv.y * s, uv.x * s + uv.y * c);
}

vec2 nova_rain_drop_layer(vec2 uv, float t) {
	vec2 screenUV = uv;

	vec2 grid = vec2(12.0, 2.0);
	float colShift = nova_rand(floor(uv.x * grid.x));
	uv.y += t + colShift;

	vec2 uvGrid = uv * grid;
	vec3 n = nova_rand3(floor(uvGrid));
	vec2 st = fract(uvGrid) - vec2(0.5, 0.0);

	float x = n.x - 0.5;
	float wiggle = screenUV.y * 20.0;
	wiggle = sin(wiggle + sin(wiggle));
	x += wiggle * (0.5 - abs(x)) * (n.y - 0.5);
	x *= 0.7;
	float y = nova_rain_saw(0.8, fract(t + n.z));
	float d = length((st - vec2(x, y)) * grid.yx);
	float mainDrop = nova_rain_s(0.4, 0.0, d);

	float r = nova_rain_s(1.0, y, st.y);
	float trail = nova_rain_s(0.2, 0.0, abs(st.x - x));
	float trailFront = nova_rain_s(-0.02, 0.02, st.y - y);
	trail *= trailFront * r;

	y = fract(screenUV.y * 10.0) + (st.y - 0.5);
	float dd = length(st - vec2(x, y));
	float droplets = nova_rain_s(0.3, 0.0, dd);
	float c = mainDrop + trailFront * droplets * r;

	return vec2(c, trail);
}

vec2 nova_rain_drops(vec2 uv, float t, float l0, float l1, float l2, float angleRad) {
	vec2 uvRot = nova_rain_rotate_to_flow(uv, angleRad);
	float s = nova_rain_static_drops(uv * 40.0, t) * l0;
	vec2 m1 = nova_rain_drop_layer(uvRot * 0.5, t) * l1;
	vec2 m2 = nova_rain_drop_layer(uvRot, t) * l2;
	float c = s + m1.x + m2.x;
	c = nova_rain_s(0.0, 1.0, c);
	float trail = m1.y + m2.y;
	return vec2(c, trail);
}

void fragment() {
	vec2 screenUV = SCREEN_UV;
	vec2 uv = vec2(SCREEN_UV.x * _Size * _Aspect, SCREEN_UV.y * _Size) * _Density;
	float t = TIME * 0.2 * _Speed;
	float angleRad = radians(_Angle);

	float l0 = nova_rain_s(0.0, 1.0, _T) * 2.0;
	float l1 = nova_rain_s(0.25, 0.75, _T);
	float l2 = nova_rain_s(0.0, 0.5, _T);

	vec2 c = nova_rain_drops(uv, t, l0, l1, l2, angleRad);
	vec2 e = vec2(1e-3, 0.0);
	float cx = nova_rain_drops(uv + e, t, l0, l1, l2, angleRad).x;
	float cy = nova_rain_drops(uv + e.yx, t, l0, l1, l2, angleRad).x;
	vec2 n = vec2(cx - c.x, cy - c.x);

	vec4 col = texture(_MainTex, screenUV + n);
	vec4 col2 = nova_tex2DGaussianBlur(_MainTex, SCREEN_PIXEL_SIZE, screenUV, 3.0 * _T) * (1.0 - 0.1 * _T);
	col = mix(col2, col, clamp(c.x + c.y, 0.0, 1.0));

	COLOR = col;
	COLOR.a = 1.0;
}
