#version 430

// This is the vertex position of the base particle!
// This is the vertex attribute set in the code, index 0.
layout (location=0) in vec3 vertexPosition;

// Input uniform values.
layout (location=0) uniform mat4 projectionMatrix;
layout (location=1) uniform mat4 viewMatrix;
layout (location=2) uniform float particleScale;

// The two buffers we will be reading from.
// We can write to them here but should not.
layout(std430, binding=0) buffer ssbo0 { vec4 positions[]; };
layout(std430, binding=1) buffer ssbo1 { vec4 velocities[]; };

// We will only output color.
out vec4 fragColor;

void main()
{
    vec3 velocity = velocities[gl_InstanceID].xyz;
    vec3 position = positions[gl_InstanceID].xyz;
    float radius = positions[gl_InstanceID].w;

    // Set color based on velocity magnitude and direction
    float speed = length(velocity);
    vec3 normalizedVel = speed > 0.001 ? normalize(velocity) : vec3(0, 0, 1);

    // Create a colorful gradient based on velocity direction and speed
    fragColor.rgb = abs(normalizedVel) * 0.7 + vec3(0.3);
    fragColor.rgb += vec3(speed * 0.1); // Add brightness based on speed
    fragColor.a = 1.0;

    // We want the particle to be a circle that always faces the camera
    // Scale the vertex by the particle's radius
    float scale = radius * particleScale;
    vec3 vertexView = vertexPosition * scale;

    // Add the particle position to the vertex (in view space).
    vertexView += (viewMatrix * vec4(position, 1)).xyz;

    // Calculate final vertex position.
    gl_Position = projectionMatrix * vec4(vertexView, 1);
}
