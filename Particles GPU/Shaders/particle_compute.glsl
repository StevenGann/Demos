// We require version 430 since it supports compute shaders.
#version 430

// This is the workgroup size. The largest size that is guaranteed by OpenGL
// to available is 1024, beyond this is uncertain.
// Might influence performance but only in advanced cases.
layout (local_size_x = 1024, local_size_y = 1, local_size_z = 1) in;

//
// Shader Storage Buffer Objects (SSBO's) can store any data and are
// declared like structs. There can only be one variable-sized array
// in an SSBO, for this reason we use multiple SSBO's to store our arrays.
//
// We do not use structures to store the particles, because an SSBO
// only has a guaranteed max size of 128 Mb.
// With 1 million particles you can store only 32 floats per particle in one SSBO.
// Therefore use multiple buffers since this example scales up to the millions.
//
layout(std430, binding=0) buffer ssbo0 { vec4 positions[]; };
layout(std430, binding=1) buffer ssbo1 { vec4 velocities[]; };
layout(std430, binding=2) buffer ssbo2 { vec4 startPositions[]; };

// Uniform values are the way in which we can modify the shader efficiently.
// These can be updated every frame efficiently.
// We use layout(location=...) but you can also leave it and query the location in Raylib.
layout(location=0) uniform float time;
layout(location=1) uniform float timeScale;
layout(location=2) uniform float deltaTime;
layout(location=3) uniform float gravity;
layout(location=4) uniform float airFriction;
layout(location=5) uniform vec2 screenSize;
layout(location=6) uniform vec2 mousePos;
layout(location=7) uniform float mouseForceRadius;
layout(location=8) uniform float mouseForceStrength;
layout(location=9) uniform int mouseButtonState; // 0=none, 1=attract, 2=repel

const float PI = 3.14159;

void main()
{
    uint index = gl_GlobalInvocationID.x;

    // Check bounds
    if (index >= positions.length()) return;

    vec3 pos = positions[index].xyz;
    vec3 vel = velocities[index].xyz;
    float radius = positions[index].w;

    // We reset the position when time is exactly zero.
    if (time == 0) {
        pos = startPositions[index].xyz;
        vel = vec3(0.0);
    }

    // Apply gravity
    vel.y += gravity;

    // Apply air friction
    vel *= airFriction;

    // Apply mouse force if mouse button is pressed
    if (mouseButtonState > 0) {
        vec2 toMouse = mousePos - pos.xy;
        float distSq = dot(toMouse, toMouse);
        float radiusSq = mouseForceRadius * mouseForceRadius;

        if (distSq < radiusSq && distSq > 0.01) {
            float dist = sqrt(distSq);
            vec2 direction = toMouse / dist;

            // For repel (right click), reverse direction
            if (mouseButtonState == 2) {
                direction = -direction;
            }

            // Calculate force strength based on distance (closer = stronger)
            float distanceFactor = 1.0 - (dist / mouseForceRadius);
            float forceStrength = mouseForceStrength * distanceFactor;

            // Apply force inversely proportional to ball mass (radius)
            float massReduction = 1.0 / radius;
            vec2 force = direction * forceStrength * massReduction;

            vel.xy += force;
        }
    }

    // Update position
    pos += vel * deltaTime;

    // Check for collisions with screen edges and apply restitution
    float wallRestitution = 0.7;

    // Horizontal bounds
    if (pos.x + radius >= screenSize.x || pos.x - radius <= 0) {
        float impactSpeed = abs(vel.x);
        float effectiveRestitution = wallRestitution * max(0.5, 1.0 - impactSpeed * 0.02);
        vel.x = -vel.x * effectiveRestitution;

        // Keep within bounds
        if (pos.x + radius > screenSize.x) pos.x = screenSize.x - radius;
        if (pos.x - radius < 0) pos.x = radius;
    }

    // Vertical bounds
    if (pos.y + radius >= screenSize.y || pos.y - radius <= 0) {
        float impactSpeed = abs(vel.y);
        float effectiveRestitution = wallRestitution * max(0.5, 1.0 - impactSpeed * 0.02);
        vel.y = -vel.y * effectiveRestitution;

        // Keep within bounds
        if (pos.y + radius > screenSize.y) pos.y = screenSize.y - radius;
        if (pos.y - radius < 0) pos.y = radius;
    }

    // Store updated values
    positions[index].xyz = pos;
    velocities[index].xyz = vel;
}
