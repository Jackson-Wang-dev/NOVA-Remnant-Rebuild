// Ported from Nova1's Assets/Nova/CGInc/Rand.cginc. Only the overloads actually needed by ported VFX
// shaders exist (see porting-guide.md M8) - add more as later phases need them rather than porting
// every overload (rand4/noise3/etc.) up front.

const float NOVA_RAND_MAGIC1 = 0.1031;
const float NOVA_RAND_MAGIC2 = 0.1030;
const float NOVA_RAND_MAGIC3 = 0.0973;
const float NOVA_RAND_MAGIC5 = 33.33;

float nova_rand(float p) {
	p = fract(p * NOVA_RAND_MAGIC1);
	p *= p + NOVA_RAND_MAGIC5;
	p *= p + p;
	return fract(p);
}

float nova_rand(vec2 p2) {
	vec3 p3 = fract(p2.xyx * NOVA_RAND_MAGIC1);
	p3 += dot(p3, p3.yzx + NOVA_RAND_MAGIC5);
	return fract((p3.x + p3.y) * p3.z);
}

float nova_rand(vec3 p3) {
	p3 = fract(p3 * NOVA_RAND_MAGIC1);
	p3 += dot(p3, p3.yzx + NOVA_RAND_MAGIC5);
	return fract((p3.x + p3.y) * p3.z);
}

vec2 nova_rand2(float p) {
	vec3 p3 = fract(vec3(p, p, p) * vec3(NOVA_RAND_MAGIC1, NOVA_RAND_MAGIC2, NOVA_RAND_MAGIC3));
	p3 += dot(p3, p3.yzx + NOVA_RAND_MAGIC5);
	return fract((p3.xx + p3.yz) * p3.zy);
}

vec2 nova_rand2(vec3 p3) {
	p3 = fract(p3 * vec3(NOVA_RAND_MAGIC1, NOVA_RAND_MAGIC2, NOVA_RAND_MAGIC3));
	p3 += dot(p3, p3.yzx + NOVA_RAND_MAGIC5);
	return fract((p3.xx + p3.yz) * p3.zy);
}

vec3 nova_rand3(vec2 p2) {
	vec3 p3 = fract(p2.xyx * vec3(NOVA_RAND_MAGIC1, NOVA_RAND_MAGIC2, NOVA_RAND_MAGIC3));
	p3 += dot(p3, p3.yzx + NOVA_RAND_MAGIC5);
	return fract((p3.xxy + p3.yzz) * p3.zyx);
}

float nova_noise(float v) {
	float a = floor(v);
	float u = v - a;
	u = u * u * (3.0 - 2.0 * u);
	return mix(nova_rand(a), nova_rand(a + 1.0), u);
}

float nova_noise(vec2 v) {
	vec2 a = floor(v);
	vec2 u = v - a;
	u = u * u * (3.0 - 2.0 * u);
	return mix(
		mix(nova_rand(a), nova_rand(a + vec2(1.0, 0.0)), u.x),
		mix(nova_rand(a + vec2(0.0, 1.0)), nova_rand(a + vec2(1.0, 1.0)), u.x),
		u.y
	);
}

float nova_noise(vec3 v) {
	vec3 a = floor(v);
	vec3 u = v - a;
	u = u * u * (3.0 - 2.0 * u);
	return mix(
		mix(
			mix(nova_rand(a), nova_rand(a + vec3(1.0, 0.0, 0.0)), u.x),
			mix(nova_rand(a + vec3(0.0, 1.0, 0.0)), nova_rand(a + vec3(1.0, 1.0, 0.0)), u.x),
			u.y
		),
		mix(
			mix(nova_rand(a + vec3(0.0, 0.0, 1.0)), nova_rand(a + vec3(1.0, 0.0, 1.0)), u.x),
			mix(nova_rand(a + vec3(0.0, 1.0, 1.0)), nova_rand(a + vec3(1.0, 1.0, 1.0)), u.x),
			u.y
		),
		u.z
	);
}

vec2 nova_noise2(vec3 v) {
	vec3 a = floor(v);
	vec3 u = v - a;
	u = u * u * (3.0 - 2.0 * u);
	return mix(
		mix(
			mix(nova_rand2(a), nova_rand2(a + vec3(1.0, 0.0, 0.0)), u.x),
			mix(nova_rand2(a + vec3(0.0, 1.0, 0.0)), nova_rand2(a + vec3(1.0, 1.0, 0.0)), u.x),
			u.y
		),
		mix(
			mix(nova_rand2(a + vec3(0.0, 0.0, 1.0)), nova_rand2(a + vec3(1.0, 0.0, 1.0)), u.x),
			mix(nova_rand2(a + vec3(0.0, 1.0, 1.0)), nova_rand2(a + vec3(1.0, 1.0, 1.0)), u.x),
			u.y
		),
		u.z
	);
}

vec2 nova_snoise2(vec3 v) {
	return nova_noise2(v) * 2.0 - 1.0;
}
