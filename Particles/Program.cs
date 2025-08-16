using Raylib_cs;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading.Tasks;

namespace Particles
{
    // Structure to represent a collision pair
    public struct CollisionPair
    {
        public int CircleA;
        public int CircleB;

        public CollisionPair(int a, int b)
        {
            CircleA = a;
            CircleB = b;
        }
    }

    class Program
    {
        const int DEFAULT_CIRCLE_COUNT = 5000;
        const int MIN_CIRCLE_COUNT = 10;
        const int MAX_CIRCLE_COUNT = 50000;

        // Performance profiling variables
        static double avgInputTime = 0.0;
        static double avgPhysicsTime = 0.0;
        static double avgCollisionTime = 0.0;
        static double avgRenderTime = 0.0;

        // Object for thread synchronization during collision resolution
        static readonly object CollisionLock = new object();

        static void Main(string[] args)
        {
            // Parse command line arguments
            int circleCount = ParseCircleCount(args);

            // Window configuration
            const int screenWidth = 1024;
            const int screenHeight = 768;
            string title = $"Particles";

            // Physics configuration
            const float gravity = 0.15f; // Gravity constant
            const float airFriction = 0.999f; // Air resistance multiplier (< 1.0 for friction)
            const float wallRestitution = 0.7f; // Wall bounce energy retention
            const float minRestitution = 0.5f; // Minimum ball restitution
            const float maxRestitution = 0.9f; // Maximum ball restitution

            // Mouse force configuration
            float forceRadius = 50.0f; // Radius of mouse influence (now adjustable)
            const float forceStrength = 9.0f; // Strength of the continuous force (reduced for continuous application)
            const float minForceRadius = 10.0f; // Minimum force radius
            const float maxForceRadius = 500.0f; // Maximum force radius
            const float radiusScrollSpeed = 5.0f; // How much radius changes per scroll step

            // Initialize window
            Raylib.InitWindow(screenWidth, screenHeight, title);
            Raylib.SetTargetFPS(60);

            // Define background color
            Color white = new Color(255, 255, 255, 255);

            // Arrays to store circle properties
            Vector2[] positions = new Vector2[circleCount];
            Vector2[] velocities = new Vector2[circleCount];
            Vector2[] velocitiesAverage = new Vector2[circleCount];
            Color[] colors = new Color[circleCount];
            float[] radii = new float[circleCount];
            float[] restitutions = new float[circleCount]; // Individual restitution per ball

            // Initialize random number generator
            Random random = new Random();

            // Initialize circles with random properties
            Console.WriteLine($"Initializing {circleCount} circles...");
            for (int i = 0; i < circleCount; i++)
            {
                if (i % 500 == 0 || i == circleCount - 1)
                {
                    Console.WriteLine($"Initializing circle {i + 1} of {circleCount}...");
                }

                // Random radius between 3 and 10
                radii[i] = (random.Next(3, 10) + random.Next(3, 10)) / 2.0f;

                // Random restitution - smaller balls tend to be bouncier
                restitutions[i] = minRestitution + (maxRestitution - minRestitution) * (1.0f - radii[i] / 10.0f) + (float)(random.NextDouble() * 0.2f - 0.1f);
                restitutions[i] = Math.Max(minRestitution, Math.Min(maxRestitution, restitutions[i]));

                // Random position (ensuring the circle is fully within the screen)
                bool validPosition = false;
                int attempts = 0;
                while (!validPosition)
                {
                    positions[i] = new Vector2(
                        random.Next((int)radii[i], screenWidth - (int)radii[i]),
                        random.Next((int)radii[i], screenHeight - (int)radii[i])
                    );

                    // Check if this position overlaps with any existing circles
                    validPosition = true;
                    for (int j = 0; j < i; j++)
                    {
                        // Optimization #2: Use squared distance during initialization as well
                        float combinedRadius = MathF.Max(radii[i], radii[j]);
                        float distanceSquared = Vector2.DistanceSquared(positions[i], positions[j]);
                        if (distanceSquared < combinedRadius * combinedRadius) // Allow for some overlap
                        {
                            validPosition = false;
                            break;
                        }
                    }

                    if (attempts > 1000)
                    {
                        validPosition = true; // Force position after too many attempts
                    }

                    attempts++;
                }

                // Random velocity between -5 and 5 (non-zero)
                float vx = 0;
                float vy = 0;
                while (Math.Abs(vx) < 1) vx = (float)(random.NextDouble() * 10 - 5);
                while (Math.Abs(vy) < 1) vy = (float)(random.NextDouble() * 10 - 5);
                velocities[i] = new Vector2(vx, vy);
                velocitiesAverage[i] = velocities[i];

                colors[i] = new Color(
                    random.Next(0, 128),  // R
                    random.Next(0, 128),  // G
                    random.Next(128, 256),  // B
                    255                     // A (fully opaque)
                );

                float hue = (positions[i].X / screenWidth) * 360.0f;

                colors[i] = ColorFromHSVA(hue, 1.0f, 1.0f, 255);
            }
            Console.WriteLine("Circles initialized.");

            // Performance profiling stopwatch
            Stopwatch profiler = new Stopwatch();

            // Main game loop
            while (!Raylib.WindowShouldClose())
            {
                // === INPUT HANDLING PHASE ===
                profiler.Restart();

                // Get mouse position
                Vector2 mousePos = Raylib.GetMousePosition();

                // Handle scroll wheel input to adjust force radius
                float wheelMovement = Raylib.GetMouseWheelMove();
                if (wheelMovement != 0)
                {
                    forceRadius += wheelMovement * radiusScrollSpeed;
                    forceRadius = Math.Max(minForceRadius, Math.Min(maxForceRadius, forceRadius));
                }

                // Check for continuous mouse interaction
                if (Raylib.IsMouseButtonDown(MouseButton.Left))
                {
                    // Left mouse button attracts balls toward the mouse
                    ApplyMouseForce(mousePos, forceRadius, forceStrength, positions, velocities, radii, circleCount, true);
                }
                else if (Raylib.IsMouseButtonDown(MouseButton.Right))
                {
                    // Right mouse button repels balls away from the mouse
                    ApplyMouseForce(mousePos, forceRadius, forceStrength, positions, velocities, radii, circleCount, false);
                }

                profiler.Stop();
                avgInputTime = (avgInputTime + profiler.Elapsed.TotalMilliseconds) / 2.0;

                // === PHYSICS UPDATE PHASE ===
                profiler.Restart();

                // Update - using Parallel.For for better performance
                Parallel.For(0, circleCount, i =>
                {
                    // Apply gravity to vertical velocity
                    velocities[i] = new Vector2(velocities[i].X, velocities[i].Y + gravity);

                    // Apply air friction to reduce velocity over time (prevents endless oscillations)
                    velocities[i] *= airFriction;

                    // Move the circle
                    positions[i] += velocities[i];

                    // Check for collisions with screen edges
                    if (positions[i].X + radii[i] >= screenWidth || positions[i].X - radii[i] <= 0)
                    {
                        // Apply velocity-dependent wall restitution
                        float impactSpeed = Math.Abs(velocities[i].X);
                        float effectiveRestitution = CalculateVelocityDependentRestitution(wallRestitution, impactSpeed);

                        velocities[i] = new Vector2(-velocities[i].X * effectiveRestitution, velocities[i].Y);

                        // Ensure the circle stays within bounds
                        if (positions[i].X + radii[i] > screenWidth)
                            positions[i] = new Vector2(screenWidth - radii[i], positions[i].Y);
                        if (positions[i].X - radii[i] < 0)
                            positions[i] = new Vector2(radii[i], positions[i].Y);
                    }
                    if (positions[i].Y + radii[i] >= screenHeight || positions[i].Y - radii[i] <= 0)
                    {
                        // Apply velocity-dependent wall restitution
                        float impactSpeed = Math.Abs(velocities[i].Y);
                        float effectiveRestitution = CalculateVelocityDependentRestitution(wallRestitution, impactSpeed);

                        velocities[i] = new Vector2(velocities[i].X, -velocities[i].Y * effectiveRestitution);

                        // Ensure the circle stays within bounds
                        if (positions[i].Y + radii[i] > screenHeight)
                            positions[i] = new Vector2(positions[i].X, screenHeight - radii[i]);
                        if (positions[i].Y - radii[i] < 0)
                            positions[i] = new Vector2(positions[i].X, radii[i]);
                    }

                    // Update average velocity
                    velocitiesAverage[i].X = (velocitiesAverage[i].X + velocities[i].X) / 2.0f;
                    velocitiesAverage[i].Y = (velocitiesAverage[i].Y + velocities[i].Y) / 2.0f;
                });

                profiler.Stop();
                avgPhysicsTime = (avgPhysicsTime + profiler.Elapsed.TotalMilliseconds) / 2.0;

                // === MULTI-THREADED COLLISION DETECTION PHASE ===
                profiler.Restart();

                // Thread-safe collection to store detected collisions
                var collisions = new ConcurrentBag<CollisionPair>();

                // Phase 1: Multi-threaded collision detection
                Parallel.For(0, circleCount, i =>
                {
                    for (int j = i + 1; j < circleCount; j++)
                    {
                        // Optimization #3: Broad Phase - Quick AABB rejection
                        float combinedRadius = radii[i] + radii[j];
                        if (Math.Abs(positions[i].X - positions[j].X) > combinedRadius ||
                            Math.Abs(positions[i].Y - positions[j].Y) > combinedRadius)
                        {
                            continue; // Skip expensive distance calculation
                        }

                        // Optimization #2: Use squared distance to avoid expensive sqrt()
                        float distanceSquared = Vector2.DistanceSquared(positions[i], positions[j]);
                        float minDistanceSquared = combinedRadius * combinedRadius;

                        // Check if circles are colliding
                        if (distanceSquared < minDistanceSquared)
                        {
                            // Add collision to the thread-safe collection
                            collisions.Add(new CollisionPair(i, j));
                        }
                    }
                });

                // Phase 2: Single-threaded collision resolution to avoid race conditions
                foreach (var collision in collisions)
                {
                    ResolveCollision(collision.CircleA, collision.CircleB, positions, velocities, radii, restitutions);
                }

                profiler.Stop();
                avgCollisionTime = (avgCollisionTime + profiler.Elapsed.TotalMilliseconds) / 2.0;

                // === RENDERING PHASE ===
                profiler.Restart();

                // Draw
                Raylib.BeginDrawing();
                Raylib.ClearBackground(Color.Gray);

                // Draw all circles
                for (int i = 0; i < circleCount; i++)
                {
                    Color c = colors[i];
                    double tint = 5.0 * velocitiesAverage[i].LengthSquared();
                    c.R = (byte)Math.Min(c.R + tint, 255);
                    c.G = (byte)Math.Min(c.G + tint, 255);
                    c.B = (byte)Math.Min(c.B + tint, 255);
                    Raylib.DrawCircleV(positions[i], radii[i], c);
                }

                // Draw mouse force radius indicator with color based on active mode
                float opacity = Math.Min(150, 50 + (forceRadius / maxForceRadius) * 100);
                Color indicatorColor;
                if (Raylib.IsMouseButtonDown(MouseButton.Left))
                {
                    // Green for attraction
                    indicatorColor = new Color((byte)0, (byte)255, (byte)0, (byte)opacity);
                }
                else if (Raylib.IsMouseButtonDown(MouseButton.Right))
                {
                    // Red for repulsion
                    indicatorColor = new Color((byte)255, (byte)0, (byte)0, (byte)opacity);
                }
                else
                {
                    // White for neutral
                    indicatorColor = new Color((byte)255, (byte)255, (byte)255, (byte)(opacity / 2));
                }
                Raylib.DrawCircleLines((int)mousePos.X, (int)mousePos.Y, forceRadius, indicatorColor);

                // Draw performance profiling information
                Raylib.DrawFPS(10, 10);
                Raylib.DrawText($"Circles: {circleCount}", 10, 35, 20, Color.White);
                Raylib.DrawText($"Input: {avgInputTime:F2}ms", 10, 60, 20, Color.White);
                Raylib.DrawText($"Physics: {avgPhysicsTime:F2}ms", 10, 85, 20, Color.White);
                Raylib.DrawText($"Collision: {avgCollisionTime:F2}ms", 10, 110, 20, Color.White);
                Raylib.DrawText($"Render: {avgRenderTime:F2}ms", 10, 135, 20, Color.White);
                Raylib.DrawText($"Total: {(avgInputTime + avgPhysicsTime + avgCollisionTime + avgRenderTime):F2}ms", 10, 160, 20, Color.Yellow);
                Raylib.DrawText($"Collisions: {collisions.Count}", 10, 185, 20, new Color(0, 255, 255, 255));

                // Draw instructions
                Raylib.DrawText("Left Click: Attract | Right Click: Repel | Scroll: Adjust Radius", 10, screenHeight - 25, 16, Color.White);

                Raylib.EndDrawing();

                profiler.Stop();
                avgRenderTime = (avgRenderTime + profiler.Elapsed.TotalMilliseconds) / 2.0;
            }

            // Clean up
            Raylib.CloseWindow();
        }

        // Parse command line arguments for circle count
        static int ParseCircleCount(string[] args)
        {


            // Check for help argument
            if (args.Length > 0 && (args[0] == "--help" || args[0] == "-h" || args[0] == "/?" || args[0] == "help"))
            {
                ShowHelp();
                Environment.Exit(0);
            }

            // If no arguments provided, use default
            if (args.Length == 0)
            {
                Console.WriteLine($"Using default circle count: {DEFAULT_CIRCLE_COUNT}");
                Console.WriteLine("Use --help for usage information.");
                return DEFAULT_CIRCLE_COUNT;
            }

            // Try to parse the first argument as circle count
            if (int.TryParse(args[0], out int circleCount))
            {
                // Validate range
                if (circleCount < MIN_CIRCLE_COUNT)
                {
                    Console.WriteLine($"Warning: Minimum circle count is {MIN_CIRCLE_COUNT}. Using {MIN_CIRCLE_COUNT}.");
                    return MIN_CIRCLE_COUNT;
                }

                if (circleCount > MAX_CIRCLE_COUNT)
                {
                    Console.WriteLine($"Warning: Maximum circle count is {MAX_CIRCLE_COUNT}. Using {MAX_CIRCLE_COUNT}.");
                    return MAX_CIRCLE_COUNT;
                }

                Console.WriteLine($"Using circle count: {circleCount}");
                return circleCount;
            }
            else
            {
                Console.WriteLine($"Error: '{args[0]}' is not a valid number.");
                Console.WriteLine($"Using default circle count: {DEFAULT_CIRCLE_COUNT}");
                Console.WriteLine("Use --help for usage information.");
                return DEFAULT_CIRCLE_COUNT;
            }
        }

        // Display help information
        static void ShowHelp()
        {
            Console.WriteLine("Usage: Particles.exe [circle_count]");
            Console.WriteLine();
            Console.WriteLine("Arguments:");
            Console.WriteLine("  circle_count    Number of circles to simulate (10-50000)");
            Console.WriteLine("                  Default: 3000");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --help, -h      Show this help information");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  DevmateTest.exe           # Run with 3000 circles");
            Console.WriteLine("  DevmateTest.exe 1000      # Run with 1000 circles");
            Console.WriteLine("  DevmateTest.exe 10000     # Run with 10000 circles");
            Console.WriteLine();
            Console.WriteLine("Controls:");
            Console.WriteLine("  Left Mouse Button         Attract circles to cursor");
            Console.WriteLine("  Right Mouse Button        Repel circles from cursor");
            Console.WriteLine("  Mouse Wheel               Adjust force radius");
            Console.WriteLine("  ESC                       Exit application");
        }

        // Apply continuous force to all balls within radius of mouse position
        static void ApplyMouseForce(Vector2 mousePos, float radius, float strength, Vector2[] positions, Vector2[] velocities, float[] radii, int circleCount, bool attract)
        {
            float radiusSquared = radius * radius; // Pre-calculate for optimization #2

            for (int i = 0; i < circleCount; i++)
            {
                // Optimization #3: Quick AABB rejection first
                if (Math.Abs(positions[i].X - mousePos.X) > radius ||
                    Math.Abs(positions[i].Y - mousePos.Y) > radius)
                {
                    continue; // Skip expensive distance calculation
                }

                // Optimization #2: Use squared distance to avoid sqrt
                Vector2 direction = positions[i] - mousePos;
                float distanceSquared = direction.LengthSquared();

                // Check if ball is within influence radius
                if (distanceSquared < radiusSquared && distanceSquared > 0.01f) // Avoid division by zero
                {
                    // Get actual distance for calculations that need it
                    float distance = MathF.Sqrt(distanceSquared);

                    // Normalize direction vector
                    direction = Vector2.Normalize(direction);

                    // For attraction, reverse the direction
                    if (attract)
                    {
                        direction = -direction;
                    }

                    // Calculate force strength based on distance (closer = stronger)
                    float distanceFactor = 1.0f - (distance / radius);
                    float forceStrength = strength * distanceFactor;

                    // Apply force inversely proportional to ball mass (radius)
                    float massReduction = 1.0f / radii[i];
                    Vector2 force = direction * forceStrength * massReduction;

                    // Add force to velocity
                    velocities[i] += force;
                }
            }
        }

        // Create a Color from HSV (Hue, Saturation, Value) and Alpha values
        static Color ColorFromHSVA(float hue, float saturation, float value, byte alpha)
        {
            // Normalize hue to 0-360 range
            hue = hue % 360.0f;
            if (hue < 0) hue += 360.0f;

            // Clamp saturation and value to 0-1 range
            saturation = Math.Max(0.0f, Math.Min(1.0f, saturation));
            value = Math.Max(0.0f, Math.Min(1.0f, value));

            float c = value * saturation;
            float x = c * (1 - Math.Abs((hue / 60.0f) % 2 - 1));
            float m = value - c;

            float r, g, b;

            if (hue >= 0 && hue < 60)
            {
                r = c; g = x; b = 0;
            }
            else if (hue >= 60 && hue < 120)
            {
                r = x; g = c; b = 0;
            }
            else if (hue >= 120 && hue < 180)
            {
                r = 0; g = c; b = x;
            }
            else if (hue >= 180 && hue < 240)
            {
                r = 0; g = x; b = c;
            }
            else if (hue >= 240 && hue < 300)
            {
                r = x; g = 0; b = c;
            }
            else
            {
                r = c; g = 0; b = x;
            }

            // Convert to 0-255 range
            byte red = (byte)Math.Round((r + m) * 255);
            byte green = (byte)Math.Round((g + m) * 255);
            byte blue = (byte)Math.Round((b + m) * 255);

            return new Color(red, green, blue, alpha);
        }

        // Calculate restitution based on impact velocity (harder hits lose more energy)
        static float CalculateVelocityDependentRestitution(float baseRestitution, float impactSpeed)
        {
            // Reduce restitution for high-speed impacts
            float velocityFactor = Math.Max(0.5f, 1.0f - impactSpeed * 0.02f);
            return baseRestitution * velocityFactor;
        }

        // Resolve collision between two circles with individual restitution values
        static void ResolveCollision(int i, int j, Vector2[] positions, Vector2[] velocities, float[] radii, float[] restitutions)
        {
            // Calculate the collision normal
            Vector2 normal = positions[j] - positions[i];
            float distance = normal.Length();

            // Avoid division by zero
            if (distance == 0) return;

            normal = Vector2.Normalize(normal);

            // Calculate relative velocity
            Vector2 relativeVelocity = velocities[j] - velocities[i];

            // Calculate relative velocity in terms of the normal direction
            float velocityAlongNormal = Vector2.Dot(relativeVelocity, normal);

            // Do not resolve if velocities are separating
            if (velocityAlongNormal > 0) return;

            // Calculate combined restitution (average of both balls)
            float combinedRestitution = (restitutions[i] + restitutions[j]) / 2.0f;

            // Apply velocity-dependent restitution for high-speed collisions
            float impactSpeed = Math.Abs(velocityAlongNormal);
            float effectiveRestitution = CalculateVelocityDependentRestitution(combinedRestitution, impactSpeed);

            // Calculate impulse scalar
            float impulseScalar = -(1 + effectiveRestitution) * velocityAlongNormal;
            impulseScalar /= 1.0f / radii[i] + 1.0f / radii[j]; // Using radius as mass approximation

            // Apply impulse
            Vector2 impulse = impulseScalar * normal;
            velocities[i] -= impulse / radii[i];
            velocities[j] += impulse / radii[j];

            // Correct position to prevent circles from getting stuck together
            float overlap = radii[i] + radii[j] - distance;
            if (overlap > 0)
            {
                // Move circles apart proportionally to their radii
                float totalRadius = radii[i] + radii[j];
                float ratio1 = radii[j] / totalRadius;
                float ratio2 = radii[i] / totalRadius;

                Vector2 correction = normal * overlap;
                positions[i] -= correction * ratio1;
                positions[j] += correction * ratio2;
            }
        }
    }
}
