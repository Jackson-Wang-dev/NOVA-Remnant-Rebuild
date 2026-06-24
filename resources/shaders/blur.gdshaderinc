// Ports Nova1's Blur.cginc. Same Gauss-Legendre quadrature point sets as Nova1 (11-point disk for
// GaussianBlur/LensBlur, 7-point line for MotionBlur/RadialBlur/RotationBlur/FadeRadialBlur), just
// written as plain functions instead of the original's WEIGHT/GRAB_PIXEL macro pairs (Godot's
// shading language preprocessor doesn't support the parameterized multi-line macros Nova1 used to
// share one offset list between differently-weighted variants).

vec4 nova_gaussian_quad(sampler2D tex, vec2 uv, vec2 kernelSize, float w1, float w2, float w3) {
	vec4 sum = vec4(0.0);
	sum += w1 * texture(tex, uv + vec2( 0.00000000,  0.00000000) * kernelSize);
	sum += w2 * texture(tex, uv + vec2( 1.58339846,  0.16642188) * kernelSize);
	sum += w2 * texture(tex, uv + vec2( 0.33102042,  1.55732861) * kernelSize);
	sum += w2 * texture(tex, uv + vec2(-1.37881659,  0.79606013) * kernelSize);
	sum += w2 * texture(tex, uv + vec2(-1.18317593, -1.06533640) * kernelSize);
	sum += w2 * texture(tex, uv + vec2( 0.64757365, -1.45447423) * kernelSize);
	sum += w3 * texture(tex, uv + vec2( 0.76697193,  0.69058463) * kernelSize);
	sum += w3 * texture(tex, uv + vec2(-0.41977765,  0.94283604) * kernelSize);
	sum += w3 * texture(tex, uv + vec2(-1.02640879, -0.10787991) * kernelSize);
	sum += w3 * texture(tex, uv + vec2(-0.21457787, -1.00950949) * kernelSize);
	sum += w3 * texture(tex, uv + vec2( 0.89379237, -0.51603127) * kernelSize);
	return sum;
}

vec4 nova_tex2DGaussianBlur(sampler2D tex, vec2 texelSize, vec2 uv, float size) {
	vec2 kernelSize = texelSize * size;
	return nova_gaussian_quad(tex, uv, kernelSize, 0.40740741, 0.05018708, 0.06833144);
}

vec4 nova_tex2DLensBlur(sampler2D tex, vec2 texelSize, vec2 uv, float size) {
	vec2 kernelSize = texelSize * size;
	return nova_gaussian_quad(tex, uv, kernelSize, 0.11111111, 0.07528061, 0.10249717);
}

vec4 nova_tex2DMotionBlur(sampler2D tex, vec2 texelSize, vec2 uv, vec2 vel) {
	vec2 kernelSize = texelSize * vel;
	vec4 sum = vec4(0.0);
	sum += 0.06474248 * texture(tex, uv + (-0.94910791) * kernelSize);
	sum += 0.13985270 * texture(tex, uv + (-0.74153119) * kernelSize);
	sum += 0.19091503 * texture(tex, uv + (-0.40584515) * kernelSize);
	sum += 0.20897959 * texture(tex, uv + ( 0.00000000) * kernelSize);
	sum += 0.19091503 * texture(tex, uv + ( 0.40584515) * kernelSize);
	sum += 0.13985270 * texture(tex, uv + ( 0.74153119) * kernelSize);
	sum += 0.06474248 * texture(tex, uv + ( 0.94910791) * kernelSize);
	return sum;
}

// Identical to nova_tex2DMotionBlur above, just under its own name. Godot's shader compiler requires
// every call site of a given function's sampler argument slot to use textures with matching
// filter/repeat/hint settings; FadeRadialBlur motion-blurs both _MainTex (hint_screen_texture) and
// _SubTex (source_color) through this same kernel, and those are fundamentally different hints, so
// they can't share one function - calling nova_tex2DMotionBlur with both trips "Sampler argument 0
// ... called more than once using textures that differ in either filter, repeat, or texture hint
// setting" at compile time. Any future two-texture effect (Phase 5) hitting the same situation should
// add another differently-named duplicate rather than reusing an existing one across mismatched hints.
vec4 nova_tex2DMotionBlurSub(sampler2D tex, vec2 texelSize, vec2 uv, vec2 vel) {
	vec2 kernelSize = texelSize * vel;
	vec4 sum = vec4(0.0);
	sum += 0.06474248 * texture(tex, uv + (-0.94910791) * kernelSize);
	sum += 0.13985270 * texture(tex, uv + (-0.74153119) * kernelSize);
	sum += 0.19091503 * texture(tex, uv + (-0.40584515) * kernelSize);
	sum += 0.20897959 * texture(tex, uv + ( 0.00000000) * kernelSize);
	sum += 0.19091503 * texture(tex, uv + ( 0.40584515) * kernelSize);
	sum += 0.13985270 * texture(tex, uv + ( 0.74153119) * kernelSize);
	sum += 0.06474248 * texture(tex, uv + ( 0.94910791) * kernelSize);
	return sum;
}
