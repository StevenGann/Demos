#version 430

in vec4 fragColor;
out vec4 finalColor;

void main()
{
    // Create a circular particle by discarding fragments outside a circle
    vec2 circCoord = 2.0 * gl_PointCoord - 1.0;
    if (dot(circCoord, circCoord) > 1.0) {
        discard;
    }

    // Output the color with some smoothing at edges
    float distance = length(circCoord);
    float alpha = 1.0 - smoothstep(0.8, 1.0, distance);
    finalColor = vec4(fragColor.rgb, fragColor.a * alpha);
}
