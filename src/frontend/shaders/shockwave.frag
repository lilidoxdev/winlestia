#version 440
// Onde de choc : déplacement radial amorti derrière un front circulaire. Alpha nul là où rien ne bouge (le bureau réel reste visible).
layout(location = 0) in vec2 qt_TexCoord0;
layout(location = 0) out vec4 fragColor;

layout(std140, binding = 0) uniform buf {
    mat4 qt_Matrix;
    float qt_Opacity;
    vec4 tint;        // m3primary
    vec2 size;        // pixels logiques de la fenêtre
    vec2 center;      // normalisé
    float time;       // secondes depuis le départ du front
    float speed;      // px logiques / s
    float amplitude;  // px logiques
    float far;        // distance du coin le plus lointain, px logiques
    float boost;      // gain de luminosité
    float tintMix;    // part de teinte
    float fade;       // 1 -> 0 en fin de vie
};

layout(binding = 1) uniform sampler2D source;

void main() {
    vec2 delta = (qt_TexCoord0 - center) * size;
    float dist = length(delta);
    vec2 dir = dist > 0.0001 ? delta / dist : vec2(0.0);
    float tl = max(0.0, time - dist / speed);
    float reached = step(0.0, time - dist / speed);
    float envelope = exp(-7.0 * tl) * (1.0 - 0.45 * dist / far) * reached;
    float disp = amplitude * sin(15.0 * tl) * envelope;

    vec2 uv = clamp(qt_TexCoord0 + dir * disp / size, vec2(0.0), vec2(1.0));
    vec3 color = texture(source, uv).rgb;
    float energy = envelope;
    color = color * (1.0 + boost * energy) + tint.rgb * tintMix * energy;

    float alpha = smoothstep(0.35, 1.6, abs(disp)) * fade * qt_Opacity;
    fragColor = vec4(color * alpha, alpha);
}
