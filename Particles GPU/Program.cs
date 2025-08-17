using Raylib_cs;
using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Particles
{
    class Program
    {
        const int DEFAULT_PARTICLE_COUNT = 102400; // Multiple of 1024 for compute shader workgroups
        const int MIN_PARTICLE_COUNT = 1024;
        const int MAX_PARTICLE_COUNT = 1024000; // 1 million max

        static void Main(string[] args)
        {
            // Parse command line arguments
            int particleCount = ParseParticleCount(args);

            // Ensure particle count is multiple of 1024 for compute shader efficiency
            particleCount = ((particleCount + 1023) / 1024) * 1024;

            // Window configuration
            const int screenWidth = 1024;
            const int screenHeight = 768;
            string title = $"GPU Particles - {particleCount:N0} particles";

            // Initialize window
            Raylib.InitWindow(screenWidth, screenHeight, title);
            Raylib.SetTargetFPS(60);

            // Load and compile compute shader
            string computeShaderCode = Raylib.LoadFileText("Shaders/particle_compute.glsl");
            uint computeShaderHandle = Rlgl.CompileShader(computeShaderCode, ShaderType.Compute);
            uint computeShaderProgram = Rlgl.LoadComputeShaderProgram(computeShaderHandle);
            Raylib.UnloadFileText(computeShaderCode);

            // Load vertex and fragment shaders for rendering
            Shader particleShader = Raylib.LoadShader("Shaders/particle_vertex.glsl", "Shaders/particle_fragment.glsl");

            // Initialize particle data
            Vector4[] positions = new Vector4[particleCount];
            Vector4[] velocities = new Vector4[particleCount];
            Vector4[] startPositions = new Vector4[particleCount];

            Random random = new Random();

            // Initialize particles with random properties
            for (int i = 0; i < particleCount; i++)
            {
                float radius = (float)(random.NextDouble() * 7 + 3); // Random radius between 3 and 10

                Vector2 pos = new Vector2(
                    (float)(random.NextDouble() * (screenWidth - 2 * radius) + radius),
                    (float)(random.NextDouble() * (screenHeight - 2 * radius) + radius)
                );

                Vector2 vel = new Vector2(
                    (float)(random.NextDouble() * 10 - 5),
                    (float)(random.NextDouble() * 10 - 5)
                );

                positions[i] = new Vector4(pos.X, pos.Y, 0, radius);
                velocities[i] = new Vector4(vel.X, vel.Y, 0, 0);
                startPositions[i] = positions[i]; // Store initial positions for reset
            }

            // Create Shader Storage Buffer Objects (SSBOs)
            uint positionSSBO = Rlgl.LoadShaderBuffer((uint)(particleCount * Marshal.SizeOf<Vector4>()), positions, BufferUsageHint.DynamicCopy);
            uint velocitySSBO = Rlgl.LoadShaderBuffer((uint)(particleCount * Marshal.SizeOf<Vector4>()), velocities, BufferUsageHint.DynamicCopy);
            uint startPositionSSBO = Rlgl.LoadShaderBuffer((uint)(particleCount * Marshal.SizeOf<Vector4>()), startPositions, BufferUsageHint.DynamicCopy);

            // Create Vertex Array Object for instanced rendering
            uint particleVAO = Rlgl.LoadVertexArray();
            Rlgl.EnableVertexArray(particleVAO);

            // Define circle vertices (we'll create a circle from a triangle fan approach)
            Vector3[] vertices = CreateCircleVertices(16); // 16-sided circle

            // Configure vertex buffer
            Rlgl.EnableVertexAttribute(0);
            uint vertexBuffer = Rlgl.LoadVertexBuffer(vertices, (uint)(vertices.Length * Marshal.SizeOf<Vector3>()), false);
            Rlgl.SetVertexAttribute(0, 3, VertexBufferType.Float, false, 0, 0);
            Rlgl.DisableVertexArray();

            // Simulation parameters
            float time = 0.0f;
            float timeScale = 1.0f;
            float gravity = 0.15f;
            float airFriction = 0.999f;
            float particleScale = 1.0f;
            float forceRadius = 50.0f;
            float forceStrength = 9.0f;

            const float minForceRadius = 10.0f;
            const float maxForceRadius = 500.0f;
            const float radiusScrollSpeed = 5.0f;

            // Main game loop
            while (!Raylib.WindowShouldClose())
            {
                float deltaTime = Raylib.GetFrameTime();
                time += deltaTime;

                // Handle input
                Vector2 mousePos = Raylib.GetMousePosition();

                // Handle scroll wheel for force radius
                float wheelMovement = Raylib.GetMouseWheelMove();
                if (wheelMovement != 0)
                {
                    forceRadius += wheelMovement * radiusScrollSpeed;
                    forceRadius = Math.Max(minForceRadius, Math.Min(maxForceRadius, forceRadius));
                }

                // Determine mouse button state
                int mouseButtonState = 0;
                if (Raylib.IsMouseButtonDown(MouseButton.Left)) mouseButtonState = 1; // Attract
                else if (Raylib.IsMouseButtonDown(MouseButton.Right)) mouseButtonState = 2; // Repel

                // Reset simulation with Space key
                if (Raylib.IsKeyPressed(KeyboardKey.Space))
                {
                    time = 0.0f;
                }

                // Compute shader pass - update particles on GPU
                {
                    Rlgl.EnableShader(computeShaderProgram);

                    // Set uniform values
                    Rlgl.SetUniform(0, time, ShaderUniformDataType.Float, 1);
                    Rlgl.SetUniform(1, timeScale, ShaderUniformDataType.Float, 1);
                    Rlgl.SetUniform(2, deltaTime, ShaderUniformDataType.Float, 1);
                    Rlgl.SetUniform(3, gravity, ShaderUniformDataType.Float, 1);
                    Rlgl.SetUniform(4, airFriction, ShaderUniformDataType.Float, 1);
                    Rlgl.SetUniform(5, new Vector2(screenWidth, screenHeight), ShaderUniformDataType.Vec2, 1);
                    Rlgl.SetUniform(6, mousePos, ShaderUniformDataType.Vec2, 1);
                    Rlgl.SetUniform(7, forceRadius, ShaderUniformDataType.Float, 1);
                    Rlgl.SetUniform(8, forceStrength, ShaderUniformDataType.Float, 1);
                    Rlgl.SetUniform(9, mouseButtonState, ShaderUniformDataType.Int, 1);

                    // Bind shader storage buffers
                    Rlgl.BindShaderBuffer(positionSSBO, 0);
                    Rlgl.BindShaderBuffer(velocitySSBO, 1);
                    Rlgl.BindShaderBuffer(startPositionSSBO, 2);

                    // Dispatch compute shader
                    int workGroups = particleCount / 1024;
                    Rlgl.ComputeShaderDispatch((uint)workGroups, 1, 1);

                    Rlgl.DisableShader();
                }

                // Rendering
                Raylib.BeginDrawing();
                Raylib.ClearBackground(Color.Black);

                // Render particles using instanced rendering
                {
                    Rlgl.EnableShader(particleShader.Id);

                    // Set matrices for 2D rendering
                    Matrix4x4 projection = Matrix4x4.CreateOrthographicOffCenter(0, screenWidth, screenHeight, 0, -1, 1);
                    Matrix4x4 view = Matrix4x4.Identity;

                    Raylib.SetShaderValueMatrix(particleShader, 0, projection);
                    Raylib.SetShaderValueMatrix(particleShader, 1, view);
                    Raylib.SetShaderValue(particleShader, 2, particleScale, ShaderUniformDataType.Float);

                    // Bind shader storage buffers for reading in vertex shader
                    Rlgl.BindShaderBuffer(positionSSBO, 0);
                    Rlgl.BindShaderBuffer(velocitySSBO, 1);

                    // Draw instanced particles
                    Rlgl.EnableVertexArray(particleVAO);
                    Rlgl.DrawVertexArrayInstanced(0, vertices.Length, particleCount);
                    Rlgl.DisableVertexArray();

                    Rlgl.DisableShader();
                }

                // Draw mouse force radius indicator
                Color indicatorColor;
                float opacity = Math.Min(150, 50 + (forceRadius / maxForceRadius) * 100);
                if (mouseButtonState == 1)
                {
                    indicatorColor = new Color(0, 255, 0, (byte)opacity); // Green for attract
                }
                else if (mouseButtonState == 2)
                {
                    indicatorColor = new Color(255, 0, 0, (byte)opacity); // Red for repel
                }
                else
                {
                    indicatorColor = new Color(255, 255, 255, (byte)(opacity / 2)); // White for neutral
                }
                Raylib.DrawCircleLines((int)mousePos.X, (int)mousePos.Y, forceRadius, indicatorColor);

                // Draw UI
                Raylib.DrawFPS(10, 10);
                Raylib.DrawText($"Particles: {particleCount:N0}", 10, 35, 20, Color.White);
                Raylib.DrawText($"Left Click: Attract | Right Click: Repel | Scroll: Adjust Radius", 10, screenHeight - 50, 16, Color.White);
                Raylib.DrawText($"Space: Reset | ESC: Exit", 10, screenHeight - 25, 16, Color.White);
                Raylib.DrawText($"Force Radius: {forceRadius:F0}", 10, 60, 16, Color.White);

                Raylib.EndDrawing();
            }

            // Cleanup
            Rlgl.UnloadShaderBuffer(positionSSBO);
            Rlgl.UnloadShaderBuffer(velocitySSBO);
            Rlgl.UnloadShaderBuffer(startPositionSSBO);
            Rlgl.UnloadVertexArray(particleVAO);
            Rlgl.UnloadVertexBuffer(vertexBuffer);
            Raylib.UnloadShader(particleShader);
            Rlgl.UnloadShaderProgram(computeShaderProgram);
            Raylib.CloseWindow();
        }

        // Create vertices for a circle
        static Vector3[] CreateCircleVertices(int segments)
        {
            Vector3[] vertices = new Vector3[segments + 2]; // +2 for center and closing vertex

            // Center vertex
            vertices[0] = new Vector3(0, 0, 0);

            // Circle vertices
            for (int i = 0; i <= segments; i++)
            {
                float angle = (float)(2.0 * Math.PI * i / segments);
                vertices[i + 1] = new Vector3(
                    (float)Math.Cos(angle),
                    (float)Math.Sin(angle),
                    0
                );
            }

            return vertices;
        }

        // Parse command line arguments for particle count
        static int ParseParticleCount(string[] args)
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
                Console.WriteLine($"Using default particle count: {DEFAULT_PARTICLE_COUNT:N0}");
                Console.WriteLine("Use --help for usage information.");
                return DEFAULT_PARTICLE_COUNT;
            }

            // Try to parse the first argument as particle count
            if (int.TryParse(args[0], out int particleCount))
            {
                // Validate range
                if (particleCount < MIN_PARTICLE_COUNT)
                {
                    Console.WriteLine($"Warning: Minimum particle count is {MIN_PARTICLE_COUNT:N0}. Using {MIN_PARTICLE_COUNT:N0}.");
                    return MIN_PARTICLE_COUNT;
                }

                if (particleCount > MAX_PARTICLE_COUNT)
                {
                    Console.WriteLine($"Warning: Maximum particle count is {MAX_PARTICLE_COUNT:N0}. Using {MAX_PARTICLE_COUNT:N0}.");
                    return MAX_PARTICLE_COUNT;
                }

                Console.WriteLine($"Using particle count: {particleCount:N0}");
                return particleCount;
            }
            else
            {
                Console.WriteLine($"Error: '{args[0]}' is not a valid number.");
                Console.WriteLine($"Using default particle count: {DEFAULT_PARTICLE_COUNT:N0}");
                Console.WriteLine("Use --help for usage information.");
                return DEFAULT_PARTICLE_COUNT;
            }
        }

        // Display help information
        static void ShowHelp()
        {
            Console.WriteLine("Usage: Particles.exe [particle_count]");
            Console.WriteLine();
            Console.WriteLine("Arguments:");
            Console.WriteLine($"  particle_count    Number of particles to simulate ({MIN_PARTICLE_COUNT:N0}-{MAX_PARTICLE_COUNT:N0})");
            Console.WriteLine($"                    Default: {DEFAULT_PARTICLE_COUNT:N0}");
            Console.WriteLine("                    Will be rounded to nearest multiple of 1024");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --help, -h        Show this help information");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine($"  Particles.exe             # Run with {DEFAULT_PARTICLE_COUNT:N0} particles");
            Console.WriteLine("  Particles.exe 50000       # Run with ~50,000 particles");
            Console.WriteLine("  Particles.exe 1000000     # Run with ~1,000,000 particles");
            Console.WriteLine();
            Console.WriteLine("Controls:");
            Console.WriteLine("  Left Mouse Button         Attract particles to cursor");
            Console.WriteLine("  Right Mouse Button        Repel particles from cursor");
            Console.WriteLine("  Mouse Wheel               Adjust force radius");
            Console.WriteLine("  SPACE                     Reset simulation");
            Console.WriteLine("  ESC                       Exit application");
            Console.WriteLine();
            Console.WriteLine("Features:");
            Console.WriteLine("  - GPU-accelerated particle simulation using compute shaders");
            Console.WriteLine("  - Instanced rendering for high performance");
            Console.WriteLine("  - Real-time physics with gravity, air friction, and wall collisions");
            Console.WriteLine("  - Interactive mouse forces");
        }
    }
}
