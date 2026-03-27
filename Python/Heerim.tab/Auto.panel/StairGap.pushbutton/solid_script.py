# -*- coding: utf-8 -*-
from Autodesk.Revit.DB import *
from Autodesk.Revit.DB.Architecture import *
from Autodesk.Revit.UI import *
from Autodesk.Revit.UI.Selection import *
from Autodesk.Revit.Exceptions import OperationCanceledException
from pyrevit import revit, DB, UI, forms, script
import os
import clr

# --- Helpers ---
def get_id_val(element_id):
    """Safe retrieval of Integer value from ElementId (Revit 2024+ support)."""
    if hasattr(element_id, "Value"):
        return element_id.Value
    return element_id.IntegerValue

# --- Window Management ---
clr.AddReference("PresentationFramework")
from System.Windows import Application

def close_existing_log_windows():
    try:
        for w in list(Application.Current.Windows):
            if w.Title == "Heerim Stair Gap Log":
                w.Close()
    except Exception:
        pass

# Force close BEFORE getting new output
close_existing_log_windows()

# output.set_title("Heerim Stair Gap Log") # Removed at user request


# --- Shared Log Config ---
import datetime
# Path to current folder (pushbutton) to find assets locally
PANEL_DIR = os.path.dirname(__file__)

LOG_FILE = os.path.join(PANEL_DIR, "stairgap.log")

def log(msg):
    timestamp = datetime.datetime.now().strftime("%H:%M:%S")
    formatted_msg = "[Solid] [{}] {}".format(timestamp, msg)
# print(formatted_msg) # Disabled to prevent output window popup

    try:
        with open(LOG_FILE, "a") as f:
            f.write(formatted_msg + "\n")
    except: pass

# History loading removed to prevent output window popup


doc = revit.doc
uidoc = revit.uidoc

# --- 1. View Restriction ---
def __context__(selection):
    # Enable only in Elevation or Section views
    if doc.ActiveView.ViewType in [ViewType.Elevation, ViewType.Section]:
        return True
    return False

# --- 2. Geometry Analysis Helpers ---
def get_solids_from_element(element):
    """Retrieves ALL Solid geometries from an element."""
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

def get_highest_run(stair):
    """Finds the Stair Run with the highest elevation."""
    if hasattr(stair, "GetStairRuns"):
        try:
            run_ids = stair.GetStairRuns()
            highest_z = -99999.0
            highest_run = None
            
            if not run_ids:
                log("Warning: Component Stair has no Runs. Trying main element.")
                return stair
            
            for rid in run_ids:
                run = doc.GetElement(rid)
                bbox = run.get_BoundingBox(None)
                if bbox:
                    z_max = bbox.Max.Z
                    if z_max > highest_z:
                        highest_z = z_max
                        highest_run = run
            return highest_run
            
        except Exception as e:
            log("Error getting runs: {}. Using main element.".format(e))
            return stair
    else:
        log("Note: Legacy Stair detected. Using main element geometry.")
        return stair

def get_soffit_face(solids):
    """Finds the bottom inclined face (soffit) of the highest stair run from a list of solids."""
    candidates = []
    
    for solid in solids:
        for face in solid.Faces:
            normal = face.ComputeNormal(UV(0.5, 0.5))
            if normal.Z < -0.1 and normal.Z > -0.99: 
                 candidates.append(face)
    
    if not candidates:
        return None
        
    def get_face_max_z(f):
        mesh = f.Triangulate()
        if mesh and mesh.Vertices:
            return max([v.Z for v in mesh.Vertices])
        return -99999.0
        
    best_face = max(candidates, key=get_face_max_z)
    return best_face

def get_first_material_id(element):
    """Retrieves the first available Material ID from an element."""
    if not element: return None
    
    if hasattr(element, "GetMaterialIds"):
        mats = element.GetMaterialIds(False)
        if mats and mats.Count > 0:
            return mats[0]
            
    def get_mat_param(param_name):
        p = element.LookupParameter(param_name)
        if p and p.HasValue:
            return p.AsElementId()
        return None
        
    mid = get_mat_param("Material") or get_mat_param("Structural Material")
    if mid and mid != ElementId.InvalidElementId:
        return mid
        
    return None

def get_stair_run_material(doc, stair, run):
    """
    Robustly retrieves the material from a Stair Run using its Type properties.
    """
    try:
        # Helper to get name for logging
        def get_mat_name(mid):
            if mid and mid != ElementId.InvalidElementId:
                m = doc.GetElement(mid)
                return m.Name if m else "Unknown"
            return "None"

        # 1. Try Run object first (if it has direct material param)
        mat_id = get_first_material_id(run)
        if mat_id: 
            log("Found Material on Run Instance: {}".format(get_mat_name(mat_id)))
            return mat_id
        
        # 2. Key Step: Get Run Type
        run_type_id = None
        if hasattr(run, "GetTypeId"):
            run_type_id = run.GetTypeId()
        elif hasattr(stair, "RunType"):
             pass
             
        if not run_type_id or run_type_id == ElementId.InvalidElementId:
             p_type = run.get_Parameter(BuiltInParameter.ELEM_TYPE_PARAM)
             if p_type and get_id_val(p_type.AsElementId()) > 0:
                 run_type_id = p_type.AsElementId()

        if run_type_id:
            run_type = doc.GetElement(run_type_id)
            if run_type:
                # A. Monolithic Material (Structural Concrete usually)
                p_mono = run_type.get_Parameter(BuiltInParameter.STAIRS_RUN_TYPE_MATERIAL)
                if p_mono and p_mono.HasValue and get_id_val(p_mono.AsElementId()) > 0:
                    mid = p_mono.AsElementId()
                    log("Found Run Type Monolithic Material: {}".format(get_mat_name(mid)))
                    return mid
                
                # B. Tread Material (Finish)
                p_tread = run_type.get_Parameter(BuiltInParameter.STAIRS_RUN_TYPE_TREAD_MATERIAL)
                if p_tread and p_tread.HasValue and get_id_val(p_tread.AsElementId()) > 0:
                    mid = p_tread.AsElementId()
                    log("Found Run Type Tread Material: {}".format(get_mat_name(mid)))
                    return mid
                    
                # C. Riser Material (Finish)
                p_riser = run_type.get_Parameter(BuiltInParameter.STAIRS_RUN_TYPE_RISER_MATERIAL)
                if p_riser and p_riser.HasValue and get_id_val(p_riser.AsElementId()) > 0:
                    mid = p_riser.AsElementId()
                    log("Found Run Type Riser Material: {}".format(get_mat_name(mid)))
                    return mid

    except Exception as e:
        log("Stair Material Warning: " + str(e))
        
    # Final Fallback: Check Stair Object itself
    mat_id = get_first_material_id(stair)
    if mat_id:
        m = doc.GetElement(mat_id)
        log("Found Stair Instance Material: {}".format(m.Name if m else "Unknown"))
    else:
        log("No Material found on Stair system.")
    return mat_id

def get_3d_view(doc, target_elem_id):
    """Finds a suitable 3D View for ray tracing."""
    if doc.ActiveView.ViewType == ViewType.ThreeD and not doc.ActiveView.IsTemplate:
        return doc.ActiveView
    
    col = FilteredElementCollector(doc).OfClass(View3D)
    candidates = []
    
    for v in col:
        if not v.IsTemplate and not v.IsAssemblyView:
            if v.Name == "{3D}": 
                return v
            candidates.append(v)
    
    if candidates: return candidates[0]
    return None

# --- 3. Family Handling ---

def load_or_get_family_symbol(doc, family_name="StairGapFiller"):
    """
    Checks if FamilySymbol is loaded. If not, tries to load from 'families' folder.
    Returns the FamilySymbol or None.
    """
    # 1. Search existing symbols
    collector = FilteredElementCollector(doc).OfClass(FamilySymbol)
    for symbol in collector:
        if symbol.FamilyName == family_name:
            if not symbol.IsActive:
                t_act = Transaction(doc, "Activate Symbol")
                t_act.Start()
                try:
                    symbol.Activate()
                    doc.Regenerate()
                    t_act.Commit()
                except Exception as e:
                    log("Error activating symbol: {}".format(e))
                    t_act.RollBack()
            return symbol

    # 2. Try to load
    # Use __file__ relative path which is safer across pyRevit versions
    try:
        current_dir = os.path.dirname(__file__)
        panel_dir = os.path.dirname(current_dir)
        family_path = os.path.join(panel_dir, "Families", family_name + ".rfa")
    except:
        # Fallback if __file__ is not available for some reason (rare)
        bundle_path = script.get_bundle_file()
        family_path = os.path.join(os.path.dirname(bundle_path), "families", family_name + ".rfa")
    
    if not os.path.exists(family_path):
        forms.alert("Family file not found:\n{}".format(family_path))
        return None
        
    t = Transaction(doc, "Load Family")
    t.Start()
    try:
        loaded_fam = clr.Reference[Family]()
        success = doc.LoadFamily(family_path, loaded_fam)
        if success:
            fam = loaded_fam.Value
            # Get first symbol
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
        else:
            log("Failed to load family.")
            t.RollBack()
            return None
    except Exception as e:
        log("Error loading family: {}".format(e))
        t.RollBack()
        return None
    return None

def create_adaptive_component(points, symbol, material_id=None):
    """
    Creates an Adaptive Component instance at the given points.
    points: List of 6 XYZ points in order [L1, L2, L3, R1, R2, R3]
    """
    try:
        if not symbol.IsActive:
            symbol.Activate()
            
        instance = AdaptiveComponentInstanceUtils.CreateAdaptiveComponentInstance(doc, symbol)
        
        # Place Points
        placement_point_ids = AdaptiveComponentInstanceUtils.GetInstancePlacementPointElementRefIds(instance)
        
        if len(placement_point_ids) != len(points):
            log("Warning: Point count mismatch. Family expects {}, Script provides {}.".format(len(placement_point_ids), len(points)))
            # Try to match minimum
        
        count = min(len(placement_point_ids), len(points))
        for i in range(count):
            pt_id = placement_point_ids[i]
            point_element = doc.GetElement(pt_id)
            point_element.Position = points[i]
            
        # Set Parameters
        p_comm = instance.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)
        if p_comm and not p_comm.IsReadOnly:
            p_comm.Set("Stair Gap Filler")
            
        if material_id:
            # Try finding generic Material parameter
            p_mat = instance.LookupParameter("Material") or instance.LookupParameter("GapMaterial")
            if p_mat and not p_mat.IsReadOnly:
                p_mat.Set(material_id)
                
        return instance

    except Exception as e:
        log("Error creating Adaptive Component: {}".format(e))
        return None

def calculate_geometry_points(stair, floor, soffit_face):
    """
    Calculates the 6 points for the wedge geometry.
    Returns list of 6 XYZ points: [L_Start, L_Slope, L_Vert, R_Start, R_Slope, R_Vert]
    """
    # 1. Soffit Orientation & Top Edge
    normal = soffit_face.ComputeNormal(UV(0.5, 0.5))
    
    edges = []
    for loop in soffit_face.EdgeLoops:
        for edge in loop:
            edges.append(edge)
            
    top_edge = None
    max_z = -99999.0
    for e in edges:
        mid = e.AsCurve().Evaluate(0.5, True)
        if mid.Z > max_z:
            max_z = mid.Z
            top_edge = e
            
    if not top_edge: 
        log("Could not find top edge of soffit.")
        return None
    
    # 2. Slope Vector
    slope_vec = normal.CrossProduct(top_edge.AsCurve().ComputeDerivatives(0.5, True).BasisX).Normalize()
    if slope_vec.Z < 0: slope_vec = -slope_vec # Ensure pointing up
    
    # 3. Ray Trace Setup
    filt = ElementCategoryFilter(BuiltInCategory.OST_Floors)
    view3d = get_3d_view(doc, floor.Id)
    if not view3d:
        forms.alert("No suitable 3D view found where the Floor is visible.")
        return None
        
    ref_intersector = ReferenceIntersector(filt, FindReferenceTarget.Face, view3d)

    def trace_direction(pt, vec):
        ctx = ref_intersector.FindNearest(pt, vec)
        if ctx:
            hit_ref = ctx.GetReference()
            if hit_ref.ElementId == floor.Id: # Strict check
                return hit_ref.GlobalPoint
        return None
    
    # 4. Trace Points
    p_start_left = top_edge.AsCurve().Evaluate(0.0, True)
    p_start_right = top_edge.AsCurve().Evaluate(1.0, True)
    
    # A. Slope Ray
    p_hit_left = trace_direction(p_start_left, slope_vec)
    p_hit_right = trace_direction(p_start_right, slope_vec)
    
    # Fallback: Horizontal Ray
    if not p_hit_left or not p_hit_right:
        log("Slope ray missed. Trying Horizontal logic...")
        h_vec = XYZ.BasisZ.CrossProduct(top_edge.AsCurve().ComputeDerivatives(0.5, True).BasisX).Normalize()
        if h_vec.DotProduct(slope_vec) < 0: h_vec = -h_vec
        
        p_hit_left = trace_direction(p_start_left, h_vec)
        p_hit_right = trace_direction(p_start_right, h_vec)
        
    if not p_hit_left or not p_hit_right:
        log("Error: Ray projection missed.")
        return None
        
    def trace_vertical(pt):
        ctx = ref_intersector.FindNearest(pt, XYZ.BasisZ)
        if ctx:
             hit_ref = ctx.GetReference()
             if hit_ref.ElementId == floor.Id:
                return hit_ref.GlobalPoint
        return None

    v_l = trace_vertical(p_start_left)
    v_r = trace_vertical(p_start_right)
    
    if not v_l or not v_r:
        log("Vertical projection did not hit the selected Upper Floor.")
        return None

    # Return 6 points ordered for our Family
    # Order: [Left Set, Right Set]
    # L1: Start, L2: Slope, L3: Vert
    return [p_start_left, p_hit_left, v_l, p_start_right, p_hit_right, v_r]

# --- 4. Main Execution ---
import atexit

# Temporary Highlight Management
highlighted_ids = []

def clear_highlights():
    if highlighted_ids:
        try:
            t = Transaction(doc, "Clear Highlights")
            t.Start()
            for eid in highlighted_ids:
                doc.ActiveView.SetElementOverrides(eid, OverrideGraphicSettings())
            t.Commit()
            uidoc.RefreshActiveView()
            highlighted_ids[:] = [] # Clear list
        except:
            pass

def highlight_element(element, color_rgb):
    try:
        t = Transaction(doc, "Highlight Element")
        t.Start()
        
        ogs = OverrideGraphicSettings()
        ogs.SetSurfaceTransparency(30)
        c = Color(color_rgb[0], color_rgb[1], color_rgb[2])
        ogs.SetProjectionLineColor(c)
        ogs.SetProjectionLineWeight(5)
        
        # Try to set solid fill
        patterns = FilteredElementCollector(doc).OfClass(FillPatternElement).ToElements()
        solid_pat = next((p for p in patterns if p.GetFillPattern().IsSolidFill), None)
        if solid_pat:
            ogs.SetSurfaceForegroundPatternId(solid_pat.Id)
            ogs.SetSurfaceForegroundPatternColor(c)
            
        doc.ActiveView.SetElementOverrides(element.Id, ogs)
        highlighted_ids.append(element.Id)
        
        t.Commit()
        uidoc.RefreshActiveView()
    except Exception as e:
        log("Highlight Msg: " + str(e))
        if t.GetStatus() == TransactionStatus.Started: t.RollBack()

# Register cleanup just in case, though try-finally is better
atexit.register(clear_highlights)

try:
    # --- CREATION MODE ---

    fam_symbol = load_or_get_family_symbol(doc, "StairGapFiller_Solid")
    if not fam_symbol:
        forms.alert("Adaptive Component Family 'StairGapFiller_Solid' is missing.\nPlease check the families folder.")
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
        tg = TransactionGroup(doc, "Create Stair Gap Solid")
        tg.Start()

        # B. Selection Loop
        stair = None
        floor = None
        
        log("Please select the Stair and the Upper Floor (in any order).")
        
        while not stair or not floor:
            if not stair and not floor:
                msg = "Select Stair OR Upper Floor (ESC to Exit)"
            elif not stair:
                msg = "Select Stair (ESC to Exit)"
            else:
                msg = "Select Upper Floor (ESC to Exit)"
                
            try:
                ref = uidoc.Selection.PickObject(ObjectType.Element, msg)
                el = doc.GetElement(ref)
                
                cat_id = get_id_val(el.Category.Id)
                stair_id = int(BuiltInCategory.OST_Stairs)
                floor_id = int(BuiltInCategory.OST_Floors)
                foundation_id = int(BuiltInCategory.OST_StructuralFoundation)
                
                if cat_id == stair_id:
                    stair = el
                    log(">> Stair Selected: {} (ID: {})".format(stair.Name, stair.Id))
                    # Highlight Stair (UI Green)
                    highlight_element(stair, (40, 167, 69))

                elif cat_id in [floor_id, foundation_id]:
                    floor = el
                    log(">> Floor Selected: {} (ID: {})".format(floor.Name, floor.Id))
                    # Highlight Floor (UI Green)
                    highlight_element(floor, (40, 167, 69))
                    
                else:
                    forms.alert("Selected element is neither a Stair nor a Floor. Please try again.")
                    
            except OperationCanceledException:
                if 'tg' in locals() and tg.GetStatus() == TransactionStatus.Started:
                    tg.RollBack()
                break # EXITS THE WHILE LOOP ON ESC
            except Exception as e:
                log("Selection Error: {}".format(e))
                if "name 'uidoc' is not defined" in str(e): break
                res = forms.alert("Error: {}\nRetry?".format(e), yes=True, no=True)
                if not res: break
        
        # If user pressed ESC, stair and floor might not be both set
        if not stair or not floor:
            break

        # C. Analyze Geometry
        run = get_highest_run(stair)
        if not run:
            forms.alert("Could not find a valid Run in the selected Stair.")
            continue # Try another stair
        
        log("Highest Run found: ID {}".format(run.Id))
        
        run_solids = get_solids_from_element(run)
        if not run_solids:
            forms.alert("Could not retrieve Solid geometry from Stair Run.")
            continue # Try another stair
            
        # D. Material Selection (Fixed to Stair)
        target_mat = get_stair_run_material(doc, stair, run)
        
        if target_mat:
            m = doc.GetElement(target_mat)
            log("Applying Stair Material: {}".format(m.Name if m else "Unknown"))
        else:
            log("No Stair material detected. Using Family Default.")

        # E. Calculate & Create
        soffit = get_soffit_face(run_solids)
        
        if soffit:
            log("Calculating points...")
            points = calculate_geometry_points(stair, floor, soffit)
            
            if points:
                t = Transaction(doc, "Create Stair Gap Filler")
                t.Start()
                
                instance = create_adaptive_component(points, fam_symbol, target_mat)
                
                if instance:
                    dist_width = points[0].DistanceTo(points[3]) # L1 to R1
                    dist_length = points[0].DistanceTo(points[1]) # L1 to L2
                    dist_depth = points[0].DistanceTo(points[2]) # L1 to L3
                    
                    def set_p(name, val):
                        p = instance.LookupParameter(name)
                        if p and not p.IsReadOnly: p.Set(val)

                    set_p("Width", dist_width)
                    set_p("Length", dist_length)
                    set_p("Depth", dist_depth)
                    set_p("Height", dist_depth) # Fallback name

                    log("Success: Gap filled with Adaptive Component. ID: {}".format(instance.Id))
                
                t.Commit()

                # --- Clear highlights and Refresh ---
                clear_highlights()
                uidoc.RefreshActiveView()

                if instance:
                    try:
                        from System.Collections.Generic import List
                        sel_ids = List[ElementId]()
                        sel_ids.Add(instance.Id)
                        uidoc.Selection.SetElementIds(sel_ids)
                    except: pass
                    
                    log("Success: Gap Filler Created and Selected. (ID: {})".format(instance.Id))
                    
                uidoc.RefreshActiveView()
                
        else:
            log("Warning: Could not identify a distinct sloped soffit face.")

        # END TRANSACTION GROUP
        if tg.GetStatus() == TransactionStatus.Started:
            tg.Assimilate()

except Exception as e:
    import traceback
    log("Error: {}".format(e))
    traceback.print_exc()
    if 'tg' in locals() and tg.GetStatus() == TransactionStatus.Started:
        tg.RollBack()

finally:
    # Always rollback if not assimilated (ensures highlights are cleared)
    if 'tg' in locals() and tg.GetStatus() == TransactionStatus.Started:
        tg.RollBack()
    # Always clear highlights at the end (as legacy fallback)
    clear_highlights()
    
    # Reset Custom Cursor
    try:
        import System.Windows.Input
        System.Windows.Input.Mouse.OverrideCursor = None
    except:
        pass
