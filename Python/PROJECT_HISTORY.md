# 📂 Heerim Extension Development History & Logic
*Documented on: 2026-02-11*

## 1. AutoTrim (Automatic Wall Trimming)

Automatically detects wall intersections and cleans up L-shaped (corner) and T-shaped (3-way) junctions.

### 🛠 Core Logic
*   **Engine:** `autotrim_engine.py` controls the overall flow, calling `solver_2way.py` or `solver_3way.py` based on the intersection type.
*   **2-Way (L-Junction):** Extends the centerlines of two walls to find the intersection point, then adjusts and joins them (`JoinGeometry`).
*   **3-Way (T-Junction):**
    1.  **Dominant Wall Selection:** Identifies the "head" of the T-junction.
    2.  **V-Tip Algorithm:** Calculates the intersection point of the *outer faces* of the other two walls (the "V-Tip").
    3.  **Geometry Cleanup:** Extends the Dominant wall exactly to this V-Tip point to fill the wedge shape perfectly, then joins it with the others.

### 📜 Key Modification Workflow

| Phase | Issue / Requirement | Action Taken & Rationale |
| :--- | :--- | :--- |
| **Initial Logic** | **"Notching"** issue where lower walls cut into higher walls. | **Removed `SwitchJoinOrder` logic.** Relied on Revit's natural join behavior but added a slight **Overlap** to prevent join failures. |
| **3-Way Geometry** | Unstable or gap-prone geometry at T-junctions. | **Implemented "V-Tip" Algorithm.** Instead of centerline extension, calculated the exact outer face intersection to sculpt the Dominant wall for a perfect fit. |
| **UI Structure** | `Radius Settings` button cluttered the panel. | **Consolidated into Shift-Click.** Removed the separate button and hidden the settings behind a **Shift-Click** action on the main button for a cleaner UI. |
| **Settings UI** | Workflow interruption by confirmation popups. | **Removed Popups.** Configured the settings dialog to apply changes immediately and proceed to trim mode without requiring user confirmation. |
| **Visualization** | Search radius was hard to see. | **Added Solid Circle preview.** Replaced line-based circle with a semi-transparent solid green circle, consistent with the DragTrim selection box. |

---

## 2. Stair Gap (Void / Solid)

Generates geometry to fill gaps (Solid) or cut slabs (Void) under stairs.

### 🛠 Core Logic
*   **Adaptive Family:** Uses `StairGapFiller_Solid.rfa` and `StairGapFiller_Void.rfa`.
*   **4-Point Placement:** Extracts 4 coordinates from the bottom corners of the stair run and places the adaptive family.
*   **Property Inheritance:** Prompts the user to inherit properties (Category/Material) from either the 'Floor' or 'Stair'.

### 📜 Key Modification Workflow

| Phase | Issue / Requirement | Action Taken & Rationale |
| :--- | :--- | :--- |
| **Geometry Method** | `DirectShape` vs `Family Instance`. | **Chosen Adaptive Family.** <br> *Rationale: Stairs vary in slope and plan shape, making flexible point-based families superior to static extrusions.* |
| **Attributes** | Ambiguity in category/material assignment. | **Added Property Selection Dialog.** Gives users control to match the generated filler with adjacent elements (Floor vs Stair). |
| **Editability** | Need for fine-tuning after generation. | **Retained Shape Handles.** Leveraged the adaptive points to allow dragging in 3D views for adjustments. (Dimension-driven editing deferred due to family structure). |
| **UI Cleanup** | Button metadata (Author) was visible. | **Cleaned `bundle.yaml`.** Removed Author fields to show only essential tooltips. |

---

## 3. UI/UX Integration (Bundle & Tooltip)

### 📜 Key Modifications
*   **Button Layout:** Removed the separator line (`---`) between `Click` and `Drag` buttons for a cohesive look.
*   **Clean Tooltips:** Removed "Author" fields from `bundle.yaml` to display only the tool description and **[Shift-Click]** instruction.
*   **Shift-Click Implementation:** Implemented logic within `script.py` to handle `__shiftclick__` and call `trim_config.py`, avoiding pyRevit's default "config dot" indicator.
