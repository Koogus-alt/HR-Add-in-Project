# -*- coding: utf-8 -*-
"""
Stair Gap Filler (Void Type)
Creates a Void adaptive component to cut intersections (gap).
"""
import sys
import os
import clr
import atexit
import tempfile
from pyrevit import script, forms

clr.AddReference("PresentationFramework")
from System.Windows import Application

clr.AddReference("RevitAPI")
clr.AddReference("RevitAPIUI")
from Autodesk.Revit.DB import *
from Autodesk.Revit.UI import *
from Autodesk.Revit.UI.Selection import *
from Autodesk.Revit.Exceptions import OperationCanceledException

doc = __revit__.ActiveUIDocument.Document
uidoc = __revit__.ActiveUIDocument

# --- Window Management ---
clr.AddReference("PresentationFramework")
from System.Windows import Application

def close_existing_log_windows():
    try:
        for w in list(Application.Current.Windows):
            if w.Title == "Heerim Stair Gap Log - Void":
                w.Close()
    except Exception:
        pass

# Force close BEFORE getting new output
close_existing_log_windows()

# Output window disabled at user request


# --- Shared Log Config ---
import datetime
# Path to current folder (pushbutton) to find assets locally
PANEL_DIR = os.path.dirname(__file__)

LOG_FILE = os.path.join(PANEL_DIR, "stairgap_void.log")

def log(msg):
    timestamp = datetime.datetime.now().strftime("%H:%M:%S")
    formatted_msg = "[Void] [{}] {}".format(timestamp, msg)
# print(formatted_msg) # Disabled to prevent output window popup

    try:
        with open(LOG_FILE, "a") as f:
            f.write(formatted_msg + "\n")
    except: pass

# --- Helpers ---

# --- Helpers ---
def get_id_val(element_id):
    if hasattr(element_id, "Value"):
        return element_id.Value
    return element_id.IntegerValue

def get_3d_view(doc, target_elem_id):
    if doc.ActiveView.ViewType == ViewType.ThreeD and not doc.ActiveView.IsTemplate:
        return doc.ActiveView
    
    col = FilteredElementCollector(doc).OfClass(View3D)
    candidates = []
    for v in col:
        if not v.IsTemplate and not v.IsAssemblyView:
            candidates.append(v)
            
    if candidates:
        # Prefer "{3D}" if present
        for v in candidates:
            if v.Name == "{3D}": return v
        return candidates[0]
    return None

def get_solids_from_element(element):
    solids = []
    opt = Options()
    opt.ComputeReferences = True
    opt.DetailLevel = ViewDetailLevel.Fine
    geo_elem = element.get_Geometry(opt)
    
    if not geo_elem: return []

    for obj in geo_elem:
        if isinstance(obj, Solid) and obj.Volume > 0:
            solids.append(obj)
        elif isinstance(obj, GeometryInstance):
            inst_geo = obj.GetInstanceGeometry()
            for inst_obj in inst_geo:
                if isinstance(inst_obj, Solid) and inst_obj.Volume > 0:
                    solids.append(inst_obj)
    return solids

def get_lowest_run(stair):
    """Finds the Stair Run with the LOWEST elevation (Start)."""
    if hasattr(stair, "GetStairRuns"):
        try:
            run_ids = stair.GetStairRuns()
            lowest_z = 99999.0
            lowest_run = None
            
            if not run_ids:
                log("Warning: Component Stair has no Runs. Trying main element.")
                return stair
            
            for rid in run_ids:
                run = doc.GetElement(rid)
                # Ensure it's a Run, not a Landing (though GetStairRuns usually returns Runs only)
                if run.Category.Id.IntegerValue != int(BuiltInCategory.OST_StairsRuns):
                    continue
                    
                bbox = run.get_BoundingBox(None)
                if bbox:
                    z_min = bbox.Min.Z
                    if z_min < lowest_z:
                        lowest_z = z_min
                        lowest_run = run
            
            if lowest_run:
                log("Lowest Run Found: ID {}".format(lowest_run.Id))
                return lowest_run
            return stair
            
        except Exception as e:
            log("Error getting runs: {}. Using main element.".format(e))
            return stair
    else:
        return stair

def load_or_get_family_symbol(doc, family_name="StairGapFiller_Void"):
    collector = FilteredElementCollector(doc).OfClass(FamilySymbol)
    for symbol in collector:
        if symbol.FamilyName == family_name:
            if not symbol.IsActive:
                t = Transaction(doc, "Activate Symbol")
                t.Start()
                symbol.Activate()
                t.Commit()
            return symbol

    # Try to load
    current_dir = os.path.dirname(__file__)
    family_path = os.path.join(current_dir, "Families", family_name + ".rfa")
    
    if not os.path.exists(family_path):
        forms.alert("Family file not found:\n{}".format(family_path))
        return None
        
    t = Transaction(doc, "Load Family")
    t.Start()
    try:
        loaded_fam = clr.Reference[Family]()
        if doc.LoadFamily(family_path, loaded_fam):
            fam = loaded_fam.Value
            # Get first symbol
            symbol_ids = fam.GetFamilySymbolIds()
            if symbol_ids and symbol_ids.Count > 0:
                first_sid = None
                for sid in symbol_ids:
                    first_sid = sid
                    break
                    
                if first_sid:
                    symbol = doc.GetElement(first_sid)
                    if not symbol.IsActive:
                        symbol.Activate()
                        doc.Regenerate()
                    t.Commit()
                    return symbol
    except: pass
    t.RollBack()
    return None

def create_adaptive_component(points, symbol):
    try:
        if not symbol.IsActive: symbol.Activate()
        instance = AdaptiveComponentInstanceUtils.CreateAdaptiveComponentInstance(doc, symbol)
        
        p_ids = AdaptiveComponentInstanceUtils.GetInstancePlacementPointElementRefIds(instance)
        count = min(len(p_ids), len(points))
        for i in range(count):
            pt = doc.GetElement(p_ids[i])
            pt.Position = points[i]
            
        p_comm = instance.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)
        if p_comm: p_comm.Set("Stair Gap Void")
        
        return instance
    except Exception as e:
        log("Error creating Void: {}".format(e))
        return None

def get_soffit_face(solids):
    candidates = []
    for solid in solids:
        for face in solid.Faces:
            normal = face.ComputeNormal(UV(0.5, 0.5))
            # Strict slope check: Must be pointing DOWN and somewhat angled (not flat horizontal)
            # Normal Z should be negative.
            # -1.0 is perfectly flat down.
            # We want sloped soffit, so -0.9 to -0.3 roughly?
            if normal.Z < -0.1 and normal.Z > -0.99: 
                 candidates.append(face)
    
    if not candidates: return None
        
    # We want the face that is LOWEST in the model
    def get_face_min_z(f):
        mesh = f.Triangulate()
        if mesh and mesh.Vertices:
            return min([v.Z for v in mesh.Vertices])
        return 99999.0
        
    best_face = min(candidates, key=get_face_min_z)
    return best_face

def calculate_geometry_points(stair, floor, soffit_face):
    """
    Calculates 6 points for Void (Bottom Cut).
    Logic: From Soffit BOTTOM edge, shoot rays DOWN to cut Lower Floor.
    """
    # 1. Soffit Normal
    normal = soffit_face.ComputeNormal(UV(0.5, 0.5))
    
    edges = []
    for loop in soffit_face.EdgeLoops:
        for edge in loop:
            edges.append(edge)
            
    # 2. Find BOTTOM Edge (Start of Stair)
    # The edge with the LOWEST Z coordinate
    bottom_edge = None
    min_z = 99999.0
    
    for e in edges:
        # Check both end points to be sure
        p1 = e.AsCurve().GetEndPoint(0)
        p2 = e.AsCurve().GetEndPoint(1)
        mid_z = (p1.Z + p2.Z) / 2.0
        
        if mid_z < min_z:
            min_z = mid_z
            bottom_edge = e
            
    if not bottom_edge: 
        log("Could not find bottom edge of soffit.")
        return None
    
    # Visual Debug: Show where we think the start edge is
    p_s = bottom_edge.AsCurve().GetEndPoint(0)
    p_e = bottom_edge.AsCurve().GetEndPoint(1)
    # visual_debug_line(p_s, p_e, (255, 0, 255)) # Magenta = Start Edge
    
    # 3. Slope Vector (Pointing DOWN)
    # Cross product of Normal and Edge Vector should give slope direction.
    # We want Z < 0 (Downwards)
    edge_vec = (p_e - p_s).Normalize()
    slope_vec = normal.CrossProduct(edge_vec).Normalize()
    
    # Robust check: Project slope_vec. Should go DOWN.
    if slope_vec.Z > 0: slope_vec = -slope_vec # Force Down
    
    # 4. Ray Trace Setup
    filt = ElementCategoryFilter(BuiltInCategory.OST_Floors)
    view3d = get_3d_view(doc, floor.Id)
    if not view3d:
        forms.alert("No suitable 3D view found where the Floor is visible.")
        return None
        
    ref_intersector = ReferenceIntersector(filt, FindReferenceTarget.Face, view3d)

    def trace_direction_bottom(pt, vec):
        # Find ALL intersections to get the bottom face
        ctxs = ref_intersector.Find(pt, vec)
        valid_hits = []
        for ctx in ctxs:
            hit_ref = ctx.GetReference()
            if hit_ref.ElementId == floor.Id:
                 valid_hits.append(hit_ref.GlobalPoint)
        
        # We need the FARTHEST point (Bottom Face)
        if valid_hits:
            # Sort by distance from start point (pt)
            valid_hits.sort(key=lambda p: p.DistanceTo(pt))
            return valid_hits[-1] # Return the last hit (Bottom)
            
        return None
    
    # 5. Trace Points (From Top Edge Downwards)
    # Caution: 'bottom_edge' is the start line.
    p_start_left = bottom_edge.AsCurve().Evaluate(0.0, True)
    p_start_right = bottom_edge.AsCurve().Evaluate(1.0, True)
    
    # A. Slope Ray Down
    p_hit_left = trace_direction_bottom(p_start_left, slope_vec)
    p_hit_right = trace_direction_bottom(p_start_right, slope_vec)
    
    # VISUAL DEBUG: Slope Rays (Green)
    # if p_hit_left: visual_debug_line(p_start_left, p_hit_left, (0, 255, 0))
    # else: visual_debug_line(p_start_left, p_start_left + slope_vec * 5, (255, 0, 0)) # Red Fail
    
    # if p_hit_right: visual_debug_line(p_start_right, p_hit_right, (0, 255, 0))
    # else: visual_debug_line(p_start_right, p_start_right + slope_vec * 5, (255, 0, 0)) # Red Fail

    if not p_hit_left or not p_hit_right:
        log("Slope ray could not find Floor Bottom. Tring simple vertical drop...")
        
    # B. Vertical Points
    def trace_vertical_bottom(pt):
        # Trace DOWN (-Z)
        hits = []
        
        # 1. Try DOWN (-Z)
        ctxs_down = ref_intersector.Find(pt, -XYZ.BasisZ)
        for ctx in ctxs_down:
            if ctx.GetReference().ElementId == floor.Id:
                hits.append(ctx.GetReference().GlobalPoint)
                
        # 2. Try UP (+Z) (In case we started below floor top but above bottom, or even below bottom?)
        # Actually, if we are below bottom, we hit bottom face from outside.
        ctxs_up = ref_intersector.Find(pt, XYZ.BasisZ)
        for ctx in ctxs_up:
             if ctx.GetReference().ElementId == floor.Id:
                hits.append(ctx.GetReference().GlobalPoint)
        
        if hits:
            # We want the LOWEST point (minimum Z) among all hits to be the Bottom Face?
            # Or the one farthest DOWN?
            # Usually bottom face has lower Z.
            hits.sort(key=lambda p: p.Z)
            return hits[0] # Lowest Z is bottom face
            
        return None

    v_l = trace_vertical_bottom(p_start_left)
    v_r = trace_vertical_bottom(p_start_right)
    
    # VISUAL DEBUG: Vertical Rays (Blue)
    # if v_l: visual_debug_line(p_start_left, v_l, (0, 0, 255))
    # else: visual_debug_line(p_start_left, p_start_left - XYZ.BasisZ * 2, (255, 0, 0)) # Red Fail (Down)
    
    # if v_r: visual_debug_line(p_start_right, v_r, (0, 0, 255))
    # else: visual_debug_line(p_start_right, p_start_right - XYZ.BasisZ * 2, (255, 0, 0)) # Red Fail (Down)

    # --- Mutual Fallback Logic ---
    # If a point missed the floor (e.g. over an open area), borrow the Z-level from its partner point.
    # This assumes the floor is flat/level at the missing spot.
    
    # 1. Slope Missing? Try Vertical Z
    if not p_hit_left and v_l:
        p_hit_left = p_start_left + slope_vec * ((p_start_left.Z - v_l.Z) / abs(slope_vec.Z))
        log("Recovered Left Slope Point using Vertical Z")
        
    if not p_hit_right and v_r:
        p_hit_right = p_start_right + slope_vec * ((p_start_right.Z - v_r.Z) / abs(slope_vec.Z))
        log("Recovered Right Slope Point using Vertical Z")

    # 2. Vertical Missing? Try Slope Z
    if not v_l and p_hit_left:
        v_l = XYZ(p_start_left.X, p_start_left.Y, p_hit_left.Z)
        log("Recovered Left Vertical Point using Slope Z")
        
    if not v_r and p_hit_right:
        v_r = XYZ(p_start_right.X, p_start_right.Y, p_hit_right.Z)
        log("Recovered Right Vertical Point using Slope Z")

    # 3. Both Missing? Absolute Fallback (Floor Level?)
    # If both failed, we can try to guess based on the OTHER side (Left <-> Right)
    if not v_l and v_r:
        v_l = XYZ(p_start_left.X, p_start_left.Y, v_r.Z)
        log("Recovered Left Vertical from Right Vertical")
    if not v_r and v_l:
        v_r = XYZ(p_start_right.X, p_start_right.Y, v_l.Z)
        log("Recovered Right Vertical from Left Vertical")
        
    if not p_hit_left and p_hit_right:
        # Project using right side Z distance
        z_dist = p_start_right.Z - p_hit_right.Z
        p_hit_left = p_start_left + slope_vec * (z_dist / abs(slope_vec.Z))
        log("Recovered Left Slope from Right Slope")
    if not p_hit_right and p_hit_left:
        z_dist = p_start_left.Z - p_hit_left.Z
        p_hit_right = p_start_right + slope_vec * (z_dist / abs(slope_vec.Z))
        log("Recovered Right Slope from Left Slope")

    if not p_hit_left: p_hit_left = v_l
    if not p_hit_right: p_hit_right = v_r
    
    # Final check for validity
    final_points = [p_start_left, p_hit_left, v_l, p_start_right, p_hit_right, v_r]
    
    for i, p in enumerate(final_points):
        if p is None:
            log("Error: Point {} calculation failed (is None). All fallbacks exhausted.".format(i))
            # Try to degrade gracefully?
            # If v_l is missing, maybe just drop down by fixed amount?
            # No, dangerous. Return None.
            return None
            
    return final_points

# --- Cleanup ---
high_ids = []
def clear_h():
    if high_ids:
        try:
            # Check if execution context allows transaction
            if doc.IsModifiable: return # Skip if already in transaction (too risky to clear now)
            
            t = Transaction(doc, "Clear Highlights")
            t.Start()
            for i in high_ids: 
                doc.ActiveView.SetElementOverrides(i, OverrideGraphicSettings())
            t.Commit()
            uidoc.RefreshActiveView()
            high_ids[:] = []
        except Exception as e:
            # log("Cleanup Warn: " + str(e))
            if 't' in locals() and t.GetStatus() == TransactionStatus.Started:
                t.RollBack()

atexit.register(clear_h)

def highlight(el, rgb):
    # Simplified Highlight
    try:
        t = Transaction(doc, "Highlight Element")
        t.Start()
        
        ogs = OverrideGraphicSettings()
        ogs.SetSurfaceTransparency(30)
        c = Color(rgb[0], rgb[1], rgb[2])
        ogs.SetProjectionLineColor(c)
        ogs.SetProjectionLineWeight(5)
        
        # Try to set solid fill
        patterns = FilteredElementCollector(doc).OfClass(FillPatternElement).ToElements()
        solid_pat = next((p for p in patterns if p.GetFillPattern().IsSolidFill), None)
        if solid_pat:
            ogs.SetSurfaceForegroundPatternId(solid_pat.Id)
            ogs.SetSurfaceForegroundPatternColor(c)
            
        doc.ActiveView.SetElementOverrides(el.Id, ogs)
        high_ids.append(el.Id)
        
        t.Commit()
        uidoc.RefreshActiveView()
    except Exception as e:
        log("Highlight Error: " + str(e))
        if t.GetStatus() == TransactionStatus.Started: t.RollBack()

# --- Debug Visualization ---
def visual_debug_line(pt_start, pt_end, color=(255, 0, 0)):
    """Draws a temporary Line for debugging rays."""
    try:
        t = Transaction(doc, "Debug Line")
        t.Start()
        
        # Create DirectShape Line
        ds = DirectShape.CreateElement(doc, ElementId(BuiltInCategory.OST_Lines))
        line = Line.CreateBound(pt_start, pt_end)
        ds.SetShape([line])
        
        # Simple Highlight (no nested trans)
        ogs = OverrideGraphicSettings()
        ogs.SetProjectionLineColor(Color(color[0], color[1], color[2]))
        ogs.SetProjectionLineWeight(6)
        doc.ActiveView.SetElementOverrides(ds.Id, ogs)
        
        t.Commit()
        uidoc.RefreshActiveView()
        
        # Add to cleanup list
        high_ids.append(ds.Id)
        
    except Exception as e:
        log("Debug Viz Error: " + str(e))
        try:
             if 't' in locals() and t.GetStatus() == TransactionStatus.Started: t.RollBack()
        except: pass

# --- Main ---
try:
    fam_symbol = load_or_get_family_symbol(doc, "StairGapFiller_Void")
    if not fam_symbol:
        forms.alert("Adaptive Component Family 'StairGapFiller_Void' is missing.\nPlease check the Void Family Guide.")
        script.exit()

    # CHECK: Is 'Cut with Voids' enabled?
    p_cut = fam_symbol.Family.get_Parameter(BuiltInParameter.FAMILY_ALLOW_CUT_WITH_VOIDS)
    if p_cut and p_cut.AsInteger() == 0:
        forms.alert("Configuration Error:\nThe loaded family 'StairGapFiller_Void' has 'Cut with Voids when Loaded' DISABLED.\n\nPlease:\n1. Open Family\n2. Go to Family Category & Parameters (Yellow Folder)\n3. Check 'Cut with Voids when Loaded'\n4. Reload into Project (Overwrite Parameter Values).")
        script.exit()

    import clr
    try:
        clr.AddReference("PresentationCore")
        import System.Windows.Input
        System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Cross
    except Exception as e:
        log("Cursor error: " + str(e))

    while True:
        # START TRANSACTION GROUP (For Single Undo)
        tg = TransactionGroup(doc, "Create Stair Gap Void")
        tg.Start()
    
        stair = None
        floor = None
        log("=== Stair Gap Void Mode ===")
        log("Please select Stair and Upper Floor (Any Order, ESC to Exit).")
    
        while not stair or not floor:
            if not stair: msg = "Select Stair (ESC to Exit)"
            else: msg = "Select Upper Floor (ESC to Exit)"
            
            try:
                ref = uidoc.Selection.PickObject(ObjectType.Element, msg)
                el = doc.GetElement(ref)
                cat_id = get_id_val(el.Category.Id)
                
                if cat_id == int(BuiltInCategory.OST_Stairs):
                    stair = el
                    log(">> Stair: {}".format(stair.Name))
                    highlight(stair, (40, 167, 69))
    
                elif cat_id in [int(BuiltInCategory.OST_Floors), int(BuiltInCategory.OST_StructuralFoundation)]:
                    floor = el
                    log(">> Floor: {}".format(floor.Name))
                    highlight(floor, (40, 167, 69))
    
            except OperationCanceledException:
                if tg.GetStatus() == TransactionStatus.Started: tg.RollBack()
                break # Exit selection loop
            except: pass
            
        if not stair or not floor:
            break # User pressed ESC, exit continuous mode
        
        run = get_lowest_run(stair)
        if not run: 
            if tg.GetStatus() == TransactionStatus.Started: tg.RollBack()
            continue # Try another stair
        
        log("Analyzing Geometry...")
        run_solids = get_solids_from_element(run)
        soffit = get_soffit_face(run_solids)
        
        if soffit:
            points = calculate_geometry_points(stair, floor, soffit)
            if points:
                t = Transaction(doc, "Create Void Gap")
                t.Start()
                inst = create_adaptive_component(points, fam_symbol)
                if inst:
                    # Set dimension parameters if they exist in family
                    dist_width = points[0].DistanceTo(points[3]) # L1 to R1
                    dist_length = points[0].DistanceTo(points[1]) # L1 to L2
                    dist_depth = points[0].DistanceTo(points[2]) # L1 to L3
                    
                    def set_p(name, val):
                        p = inst.LookupParameter(name)
                        if p and not p.IsReadOnly: p.Set(val)
    
                    set_p("Width", dist_width)
                    set_p("Length", dist_length)
                    set_p("Depth", dist_depth)
                    set_p("Height", dist_depth)
    
                    log("Success: Void Geometry Created. ID: {}".format(inst.Id))
                    
                    # Verify regeneration to ensure geometry is ready for cutting
                    doc.Regenerate()
                    
                    # Try to CUT the floor with the void instance
                    try:
                        can_cut = InstanceVoidCutUtils.CanBeCutWithVoid(floor)
                        if can_cut:
                             InstanceVoidCutUtils.AddInstanceVoidCut(doc, floor, inst)
                             log("Success: Floor Cut with Void.")
                        else:
                            log("Warning: Floor cannot be cut (CanBeCutWithVoid=False). Check Family settings.")
                    except Exception as cut_err:
                        log("Cut Warning: " + str(cut_err))
                        
                t.Commit()
    
                # --- Clear highlights and Refresh ---
                clear_h()
                uidoc.RefreshActiveView()
    
                if inst:
                    # Auto-select the created element for UX feedback
                    try:
                        from System.Collections.Generic import List
                        sel_ids = List[ElementId]()
                        sel_ids.Add(inst.Id)
                        uidoc.Selection.SetElementIds(sel_ids)
                    except: pass
                    
                    log("Success: Void Created and Selected. (ID: {})".format(inst.Id))
                    
                uidoc.RefreshActiveView()
                
        else:
            log("Error: Could not determine geometry.")
            
        # END TRANSACTION GROUP
        if tg.GetStatus() == TransactionStatus.Started:
            tg.Assimilate() # Merge all sub-transactions into one undo item

except Exception as e:
    import traceback
    log("Error: " + str(e))
    traceback.print_exc()
    if 'tg' in locals() and tg.GetStatus() == TransactionStatus.Started:
        tg.RollBack()

finally:
    # Ensure cleanup runs (even if error)
    if 'tg' in locals() and tg.GetStatus() == TransactionStatus.Started:
        tg.RollBack()
    clear_h()
    
    # Reset Custom Cursor
    try:
        import System.Windows.Input
        System.Windows.Input.Mouse.OverrideCursor = None
    except:
        pass
