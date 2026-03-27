# Heerim Extension - Future Roadmap

Based on the current Auto Trim functionality, here are several high-value features that could further enhance the Revit modeling experience.

## 1. Curved Wall Support (Arc/Spline)
Currently, the trim logic primarily supports straight lines (`DB.Line`).
- **Goal**: Extend `heerim_geom.py` and `layered_trim_logic.py` to calculate intersections and offsets for curved walls.
- **Benefit**: Essential for organic architectural designs.

## 2. Global View Cleanup (Auto-Trim All)
Instead of clicking or dragging, a tool to scan the entire active view.
- **Goal**: Identify all wall endpoints within a specified tolerance that are not joined and apply the trim/join logic automatically.
- **Benefit**: Massive time saver for model auditing and cleanup.

## 3. Gap Identification Tool (Visual Diagnostics)
A diagnostic tool that doesn't modify anything but highlights problems.
- **Goal**: Use AVF (Analysis Visualization Framework) or DirectShape to highlight wall ends that are close to each other but not joined.
- **Benefit**: Provides a "Health Check" for the BIM model geometry.

## 4. Wall Join Priority Presets
Allow administrators or users to define which wall types "win" in a T-junction.
- **Goal**: A UI where users can rank wall types (e.g., Structural Concrete > Partition). The tool will automatically set the Join Order based on these presets.
- **Benefit**: Ensures consistency across large projects and teams.

## 5. Sliver Wall Detection & Removal
Trimming sometimes results in tiny wall segments (e.g., < 1mm).
- **Goal**: Automatically detect and delete wall elements whose length is below a specific threshold after a trim operation.
- **Benefit**: Keeps the model clean and prevents "Line is too short" errors in Revit.
