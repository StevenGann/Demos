# Particles

A real-time physics simulation of particles with interactive mouse controls, built using Raylib-cs for C#.

## Features

### Physics Simulation
- **Multi-threaded physics engine** using `Parallel.For` for optimal performance
- **Realistic collision detection** between circles and walls
- **Gravity and air resistance** simulation
- **Velocity-dependent restitution** (energy loss based on impact speed)
- **Individual circle properties** (size, mass, bounciness)

### Interactive Controls
- **Left Mouse Button**: Attract circles toward cursor
- **Right Mouse Button**: Repel circles away from cursor
- **Mouse Wheel**: Adjust force radius (10-500 pixels)
- **ESC**: Exit application

### Performance Optimization
- **Broad-phase collision detection** using AABB (Axis-Aligned Bounding Box) rejection
- **Squared distance calculations** to avoid expensive sqrt operations
- **Thread-safe collision detection** with single-threaded resolution
- **Real-time performance profiling** displaying timing for each simulation phase

### Visual Features
- **Dynamic color coding** based on circle velocity
- **Force radius indicator** with color-coded interaction modes
- **Real-time performance metrics** display
- **Smooth 60 FPS rendering**

## Usage

### Command Line Options
```bash
Particles.exe [circle_count]
```

- `circle_count`: Number of circles to simulate (10-50,000)
- Default: 3,000 circles
- Use `--help` or `-h` for detailed usage information

### Examples
```bash
Particles.exe           # Run with 3000 circles
Particles.exe 1000      # Run with 1000 circles
Particles.exe 10000     # Run with 10000 circles
```

## Technical Implementation

### Architecture
The simulation uses a four-phase game loop:

1. **Input Processing**: Handle mouse input and user interactions
2. **Physics Update**: Apply forces, move objects, handle wall collisions
3. **Collision Detection/Resolution**: Multi-threaded detection, single-threaded resolution
4. **Rendering**: Draw all objects and UI elements

### Performance Characteristics
- **Scalability**: Handles 10-50,000 circles with multi-threading
- **Optimization**: Multiple performance optimization techniques
- **Profiling**: Real-time performance monitoring
- **Stability**: Stable physics simulation with energy conservation

### Physics Constants
- **Gravity**: 0.2 (downward acceleration)
- **Air Friction**: 0.995 (velocity dampening)
- **Wall Restitution**: 0.7 (energy retention on wall bounces)
- **Circle Restitution**: 0.5-0.9 (individual bounce characteristics)

## Dependencies
- **Raylib-cs**: Graphics and input handling
- **.NET Core/Framework**: C# runtime environment
- **System.Threading.Tasks**: Multi-threading support
- **System.Numerics**: Vector mathematics

## Performance Notes
- Higher circle counts require more processing power
- Multi-threading provides significant performance benefits
- Real-time metrics help identify performance bottlenecks
- Optimizations include AABB rejection and squared distance calculations
