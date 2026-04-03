# Wafer Aligner Simulator

A professional-grade WPF application for simulating optical edge sensor behavior in semiconductor wafer alignment systems. This tool provides real-time visualization and data export capabilities for wafer edge detection scenarios, supporting both standard wafers and SEMI G74 tape frame substrates.

![.NET](https://img.shields.io/badge/.NET-10.0-blue)
![Platform](https://img.shields.io/badge/platform-Windows-lightgrey)
![WPF](https://img.shields.io/badge/UI-WPF-blue)

## Overview

The Wafer Aligner Simulator is designed for semiconductor equipment engineers, process developers, and alignment algorithm designers who need to understand, test, and validate wafer edge detection systems. The simulator accurately models optical edge sensors as they scan rotating wafers, accounting for real-world imperfections and substrate variations.

## Key Features

### 📊 Real-Time Simulation
- **Live sensor visualization**: Watch the sensor reading update in real-time as the wafer rotates
- **Configurable chuck speed**: 1-6000 RPM with adjustable sampling rates up to 100 kHz
- **Interactive controls**: Start/stop simulation and manual angle stepping

### 🎯 Substrate Support

#### Standard Wafers
- **Diameter range**: 50-450 mm (supports all common wafer sizes: 100mm, 150mm, 200mm, 300mm, 450mm)
- **Edge features**:
  - **Notch**: V-shaped edge marker with adjustable depth (0-3mm) and angular width (0-30°)
  - **Single Flat**: SEMI-standard primary flat with configurable length and orientation
  - **Double Flat**: Primary and secondary flats for crystal orientation marking

#### Tape Frame Substrates (SEMI G74)
- **Frame geometries**: 
  - 200mm wafer frame: 228mm outer diameter, 194mm inner opening
  - 300mm wafer frame: 400mm outer diameter, 345.52mm inner opening
- **Rounded square profile**: Four flats at 0°, 90°, 180°, 270° connected by circular arcs
- **Alignment notches**: 
  - V-notch and U-notch shapes
  - Multiple notches supported per frame
  - Independent depth and width control for each notch

### 🔬 Physical Imperfections Modeling

The simulator accurately models real-world wafer conditions:

| Imperfection | Description | Impact |
|-------------|-------------|---------|
| **Bow** | Spherical warpage (concave/convex) | Uniform radial offset across all angles |
| **Potato Chip (Saddle)** | Bi-directional warp | 2θ sinusoidal radial variation |
| **Edge Chips** | Localized edge damage | User-definable position, depth, and width |
| **Centering Error** | X/Y offset from chuck center | Sinusoidal modulation of sensor reading |
| **Sensor Noise** | Random measurement uncertainty | Gaussian white noise (configurable σ) |

### 📈 Visualization

#### 1. Top-Down Wafer View
- Real-time rotation visualization
- Wafer/frame contour with all features rendered
- Sensor position indicator (fixed radial line)
- Center offset display
- Color-coded edge features

#### 2. Sensor Bar Display
- Vertical 0-28mm measurement range
- Current edge position highlighted
- Ideal centerline (14mm) reference
- Color gradient indicating measurement zone

#### 3. Sensor Chart (Angle vs. Reading)
- Full 360° rotation trace
- Grid overlay with angle/value labels
- Current position cursor
- Automatic scaling
- Feature identification markers

### 💾 Data Export

#### CSV Export
- **Configurable rotation count**: Export 1-N full rotations
- **High-resolution sampling**: Up to 100,000 samples per second
- **Format**: `SampleIndex, TimeMs, AngleDeg, SensorValueMm`
- **Use cases**: Algorithm development, offline analysis, machine learning training data

#### Preset Management
- **Save/Load configurations**: JSON-based preset files
- **Stores all parameters**: Substrate type, geometry, imperfections, chips, frame notches
- **Scenario libraries**: Build collections of test cases for systematic validation

## Technical Details

### Sensor Model

The simulator implements a **fixed-position optical edge sensor** with these characteristics:

- **Measurement Range**: 0-28 mm radial distance
- **Ideal Position**: 14 mm (centered)
- **Measurement Principle**: Perpendicular line-of-sight to chuck center
- **Geometry**: As the wafer rotates, the sensor measures the radial distance from the chuck center to the wafer edge along a fixed radial line

### Edge Detection Algorithm

The core sensor reading calculation uses high-precision geometric ray-casting:

```
SensorReading = ComputeIntersection(SensorLine, WaferEdge, ChuckAngle)
```

Where:
- `SensorLine`: Fixed radial line at sensor position
- `WaferEdge`: Polar edge contour computed from base geometry + features + imperfections
- `ChuckAngle`: Current rotational position

The edge radius at any angle incorporates:
1. **Base geometry**: Circular wafer or rounded-square tape frame
2. **Edge features**: Notch/flats with smooth cosine-bell or geometric profiles
3. **Bow/warp**: Radial offset modulation
4. **Chips**: Localized depth cuts
5. **Center offset**: Coordinate transformation
6. **Noise**: Random Gaussian perturbation

### Coordinate System

- **Origin**: Chuck rotation center
- **Angle**: 0° = right (+X), 90° = up (+Y), increases counter-clockwise
- **Units**: Millimeters (linear), degrees (angular)
- **Sensor Position**: Fixed radial line (angle updates with chuck rotation)

## User Interface Layout

```
┌─────────────────────────────────────────────────────────┐
│ Wafer Aligner Simulator                                 │
├──────────┬──────────────────────────────┬──────┬────────┤
│          │                              │      │ Sensor │
│          │   Top-Down Wafer View        │      │  Bar   │
│  Param   │   (Rotation Animation)       │      │ 0-28mm │
│  Panel   │                              │      │        │
│          ├──────────────────────────────┤      │        │
│  • Sub-  │   Sensor Chart               │      │        │
│    strate│   (Angle vs. Reading)        │      │        │
│  • Edge  │                              │      │        │
│    Feat. │                              │      │        │
│  • Imper-│                              │      │        │
│    fect. │                              │      │        │
│  • Chips │                              │      │        │
│  • Frame │                              │      │        │
│    Notch │                              │      │        │
│  • Chuck │                              │      │        │
│  • Export│                              │      │        │
│  • Preset│                              │      │        │
└──────────┴──────────────────────────────┴──────┴────────┘
```

## Parameter Reference

### Substrate Parameters

| Parameter | Range | Default | Description |
|-----------|-------|---------|-------------|
| Wafer Diameter | 50-450 mm | 300 mm | Substrate base diameter |
| Tape Frame | Toggle | Off | Enable SEMI G74 tape frame mode |

### Edge Feature Parameters

#### Notch Mode
| Parameter | Range | Default | Description |
|-----------|-------|---------|-------------|
| Notch Start | 0-359° | 0° | Angular position of notch center |
| Notch Depth | 0-3 mm | 1.0 mm | Maximum depth of V-cut |
| Notch Half-Width | 0-30° | 0.2° | Half-angle width (total = 2×) |

#### Flat Mode
| Parameter | Range | Default | Description |
|-----------|-------|---------|-------------|
| Primary Flat Angle | 0-359° | 0° | Angular position of flat center |
| Primary Flat Length | 0-200 mm | SEMI Std | Chord length of primary flat |
| Secondary Flat Angle | 0-359° | 90° | Angular position of secondary flat |
| Secondary Flat Length | 0-200 mm | SEMI Std | Chord length of secondary flat |

### Imperfection Parameters

| Parameter | Range | Default | Description |
|-----------|-------|---------|-------------|
| Noise | 0-1 mm | 0.02 mm | Gaussian measurement noise (σ) |
| Bow | ±50 mm | 0 mm | Spherical warpage (+convex, -concave) |
| Potato Chip | ±50 mm | 0 mm | Saddle warp amplitude |
| Offset X | ±50 mm | 0 mm | Horizontal centering error |
| Offset Y | ±50 mm | 0 mm | Vertical centering error |

### Chuck Parameters

| Parameter | Range | Default | Description |
|-----------|-------|---------|-------------|
| Speed | 1-6000 RPM | 60 RPM | Rotation rate |
| Sampling Rate | 10-100,000 Hz | 1000 Hz | Sensor acquisition frequency |

### Edge Chips

- **Add/Remove**: Dynamic list of chip locations
- **Per-Chip Parameters**:
  - Angle (0-359°)
  - Depth (0-10 mm)
  - Width (0-30°)

### Tape Frame Notches (Frame Mode Only)

- **Add/Remove**: Dynamic list of alignment notches
- **Per-Notch Parameters**:
  - Angle (0-359°)
  - Depth (0-10 mm)
  - Width (0-50 mm linear)
  - Shape (V-notch / U-notch)

## Workflow Examples

### 1. Standard Notch Detection Algorithm Development
1. Set wafer diameter (e.g., 300mm)
2. Configure notch: depth=1.0mm, width=0.2°, angle=0°
3. Add realistic noise: 0.02mm
4. Start simulation at 60 RPM
5. Export CSV with 5 rotations at 1000 Hz
6. Import data into MATLAB/Python for algorithm testing

### 2. Flat-Finding Robustness Testing
1. Select "Flat" mode
2. Set primary flat: length=57.5mm (SEMI 200mm std)
3. Add centering errors: X=2mm, Y=-1.5mm
4. Add bow: -5mm (concave)
5. Observe how offset affects flat detection symmetry
6. Save preset as "200mm_flat_offset_test.json"

### 3. Tape Frame Alignment Notch Characterization
1. Enable "Tape Frame (SEMI G74)"
2. Set wafer diameter: 300mm
3. Add V-notch: angle=78°, depth=3mm, width=6mm
4. Add U-notch: angle=102°, depth=3mm, width=6mm
5. Manually step through 75-105° to examine notch profiles
6. Export high-resolution data (10 kHz sampling)

### 4. Edge Chip Impact Analysis
1. Configure standard 200mm wafer with notch
2. Add chip: angle=45°, depth=0.5mm, width=1°
3. Add chip: angle=180°, depth=1.2mm, width=2°
4. Compare clean vs. chipped sensor traces
5. Assess false-positive notch detection risk

## Physics and Mathematics

### Notch Profile (Cosine-Bell)

For a notch centered at angle θ₀ with depth d and half-width w:

```
r(θ) = r₀ - d × (1 - |t|)    if |Δθ| < w
       r₀                      otherwise

where: t = (θ - θ₀) / w
       Δθ = angle_difference(θ, θ₀)
```

### Flat Geometry

For a flat of chord length L on a wafer of radius r:
- Flat depth: `d = r - √(r² - (L/2)²)`
- Angular span: `2 × arcsin(L / (2r))`

### Bow and Warp

```
r_effective(θ) = r_base + bow + potato × cos(2θ)
```

### Center Offset Transformation

For offset (x₀, y₀) from chuck center:
```
r_sensor(θ) = r_wafer(θ') + Δr_offset
θ' = θ + Δθ_offset

where geometric ray-casting solves for intersection
```

### Tape Frame Rounded Square

Four flats at 0°, 90°, 180°, 270° with perpendicular distance `d` from center:
- On flat: `r(θ) = d / cos(θ_relative)`
- On arc: `r(θ) = R` (corner arc radius)
- Transition angle: `arccos(d / R)`

## System Requirements

- **OS**: Windows 10/11 (x64)
- **.NET**: .NET 10.0 Runtime (Windows Desktop)
- **Display**: 1400×850 minimum resolution recommended
- **Memory**: ~50 MB
- **Graphics**: Hardware-accelerated WPF rendering (any modern GPU)

## Building from Source

### Prerequisites
- Visual Studio 2026 (or compatible)
- .NET 10.0 SDK
- Windows Desktop development workload

### Build Steps
```bash
git clone https://github.com/iamJohnnySam/AlignerSimulator.git
cd AlignerSimulator
dotnet build
dotnet run --project AlignerSimulator
```

### Project Structure
```
AlignerSimulator/
├── SimulatorViewModel.cs      # Core simulation logic & data model
├── WaferGeometry.cs            # Pure math engine for edge calculations
├── SimulatorPreset.cs          # Configuration serialization models
├── MainWindow.xaml             # UI layout definition
├── MainWindow.xaml.cs          # UI code-behind
├── SensorBarControl.cs         # Vertical sensor bar custom control
├── WaferTopViewControl.cs      # Top-down wafer view custom control
├── SensorChartControl.cs       # Angle-vs-reading chart custom control
├── Converters.cs               # WPF data binding value converters
└── RelayCommand.cs             # Command pattern implementation
```

## Use Cases

### Equipment Development
- **Pre-build simulation**: Validate sensor specifications before hardware fabrication
- **Optical path optimization**: Determine required sensor resolution and range
- **Edge cases**: Test algorithm behavior with damaged/warped wafers

### Process Engineering
- **Alignment strategy**: Evaluate notch vs. flat detection for specific processes
- **Tolerance analysis**: Assess impact of wafer bow/warp on alignment accuracy
- **Throughput optimization**: Balance chuck speed vs. sampling rate for cycle time

### Algorithm Development
- **Training data generation**: Create labeled datasets for ML-based edge detection
- **Benchmark datasets**: Standardized test cases for algorithm comparison
- **Regression testing**: Validate algorithm changes against known scenarios

### Education & Training
- **Concept demonstration**: Visual understanding of wafer alignment principles
- **Operator training**: Familiarize technicians with sensor behavior
- **Engineering education**: Teach geometric measurement techniques

## Limitations and Assumptions

1. **2D Model**: Assumes perfect planarity at sensor line (no Z-height variation modeling)
2. **Optical Simplification**: Does not model diffraction, reflection, or material optical properties
3. **Rigid Body**: No dynamic effects (vibration, chuck wobble, elastic deformation)
4. **Single Sensor**: Only one fixed radial sensor position modeled
5. **Ideal Timing**: No jitter or sampling synchronization errors

## Troubleshooting

**Q: Simulation appears frozen**  
A: Check that chuck speed is non-zero and Start has been clicked. Extremely slow speeds (<1 RPM) may appear static.

**Q: Sensor reading is always 14mm**  
A: Wafer is perfectly centered with no features. Add a notch, flat, offset, or chip to see variation.

**Q: Exported CSV has unexpected sample count**  
A: Sample count = `(SamplingRateHz × 60 / ChuckSpeedRpm) × ExportRotations`. Verify parameters.

**Q: Frame mode shows jagged contour**  
A: This is expected for the rounded square profile. Increase wafer diameter visualization to see smooth arcs.

**Q: Cannot load preset file**  
A: Ensure JSON file is not corrupted and matches schema. Check for manual edits that broke syntax.

## Contributing

Contributions are welcome! Areas for enhancement:
- 3D wafer surface topology modeling
- Multi-sensor configurations
- Advanced noise models (1/f, shot noise)
- Real hardware data import/comparison
- Automated test case generation
- Performance optimizations for ultra-high sampling rates

## License

This project is open-source. See LICENSE file for details.

## Authors

- **Johnny Sam** - Initial development and maintenance

## Acknowledgments

- SEMI standards committee for wafer/frame specifications
- Semiconductor equipment industry for alignment system domain knowledge

## References

- SEMI M1: Specifications for Polished Monocrystalline Silicon Wafers
- SEMI G74: Specification for Tape Frame Handling of Semiconductor Wafers
- SEMI M12: Specification for Wafer Flat Length and Orientation

---

**Version**: 1.0  
**Last Updated**: 2025  
**Contact**: https://github.com/iamJohnnySam