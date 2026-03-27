# -*- coding: utf-8 -*-
import Autodesk.Revit.DB as DB
from pyrevit import output
import heerim_geom

# Initialize output for colored logs
out = output.get_output()

def get_wall_height_range(wall):
    doc = wall.Document
    base_offset = wall.get_Parameter(DB.BuiltInParameter.WALL_BASE_OFFSET).AsDouble()
    top_offset = wall.get_Parameter(DB.BuiltInParameter.WALL_TOP_OFFSET).AsDouble()
    unconnected_height = wall.get_Parameter(DB.BuiltInParameter.WALL_USER_HEIGHT_PARAM).AsDouble()
    base_level = doc.GetElement(wall.LevelId)
    base_elev = base_level.Elevation if base_level else 0.0
    z_min = base_elev + base_offset
    top_level_id = wall.get_Parameter(DB.BuiltInParameter.WALL_HEIGHT_TYPE).AsElementId()
    if top_level_id == DB.ElementId.InvalidElementId:
        z_max = z_min + unconnected_height
    else:
        top_level = doc.GetElement(top_level_id)
        z_max = (top_level.Elevation if top_level else z_min) + top_offset
    return z_min, z_max

def check_z_overlap(wall1, wall2):
    min1, max1 = get_wall_height_range(wall1)
    min2, max2 = get_wall_height_range(wall2)
    overlay = min(max1, max2) - max(min1, min2)
    return overlay > 0.001, (min1, max1), (min2, max2)

def match_wall_height(stem_wall, boundary_wall):
    try:
        stem_wall.LevelId = boundary_wall.LevelId
        b_offset = boundary_wall.get_Parameter(DB.BuiltInParameter.WALL_BASE_OFFSET).AsDouble()
        stem_wall.get_Parameter(DB.BuiltInParameter.WALL_BASE_OFFSET).Set(b_offset)
        top_type = boundary_wall.get_Parameter(DB.BuiltInParameter.WALL_HEIGHT_TYPE).AsElementId()
        stem_wall.get_Parameter(DB.BuiltInParameter.WALL_HEIGHT_TYPE).Set(top_type)
        if top_type == DB.ElementId.InvalidElementId:
            b_height = boundary_wall.get_Parameter(DB.BuiltInParameter.WALL_USER_HEIGHT_PARAM).AsDouble()
            stem_wall.get_Parameter(DB.BuiltInParameter.WALL_USER_HEIGHT_PARAM).Set(b_height)
        else:
            b_t_offset = boundary_wall.get_Parameter(DB.BuiltInParameter.WALL_TOP_OFFSET).AsDouble()
            stem_wall.get_Parameter(DB.BuiltInParameter.WALL_TOP_OFFSET).Set(b_t_offset)
        return True
    except: return False

def solve_trim_points(stem_wall, boundary_wall, pick_point):
    c_stem = stem_wall.Location.Curve
    c_bound = boundary_wall.Location.Curve
    inter_pt = heerim_geom.get_intersection(c_stem, c_bound)
    if not isinstance(inter_pt, DB.XYZ): return None, None, None
    
    # Calculate offset to the face of the boundary wall
    try:
        b_width = boundary_wall.Width
    except:
        b_width = boundary_wall.WallType.Width
        
    # Get direction vector of stem wall pointing AWAY from intersection towards the stem
    p0 = c_stem.GetEndPoint(0)
    p1 = c_stem.GetEndPoint(1)
    
    # Find which end is the far end
    dist0 = p0.DistanceTo(inter_pt)
    dist1 = p1.DistanceTo(inter_pt)
    
    if dist0 > dist1:
        # p0 is far end, p1 is near intersection
        stem_vec = (p0 - inter_pt).Normalize()
    else:
        stem_vec = (p1 - inter_pt).Normalize()
        
    face_pt = inter_pt + (stem_vec * (b_width / 2.0))
    
    return face_pt, "Face Line (면)", "#808080"

def align_layered_wall(doc, stem_wall, boundary_wall, pt_ref, area_outline, dominant_wall=None):
    """
    pt_ref: The point used to judge WHICH end of the stem wall to move.
    dominant_wall: Kept for signature compatibility, but ignored for auto-join logic.
    """
    try:
        overlap, _, _ = check_z_overlap(stem_wall, boundary_wall)
        if not overlap: match_wall_height(stem_wall, boundary_wall)
        final_pt, label, color = solve_trim_points(stem_wall, boundary_wall, pt_ref)
        if not final_pt: return False

        line = stem_wall.Location.Curve
        p0, p1 = line.GetEndPoint(0), line.GetEndPoint(1)
        is_p0_target = p0.DistanceTo(final_pt) < p1.DistanceTo(final_pt)

        # Unjoin first if it was already joined to reset the bounding box/join cache
        try:
            if DB.JoinGeometryUtils.AreElementsJoined(doc, stem_wall, boundary_wall):
                DB.JoinGeometryUtils.UnjoinGeometry(doc, stem_wall, boundary_wall)
        except: pass
        
        # Move the wall to the exact calculated face while maintaining original curve direction
        if is_p0_target:
            stem_wall.Location.Curve = DB.Line.CreateBound(final_pt, p1)
            near_idx = 0
        else:
            stem_wall.Location.Curve = DB.Line.CreateBound(p0, final_pt)
            near_idx = 1
            
        doc.Regenerate()
        
        # Enforce exact face trim by disallowing Revit's buggy auto-snap AFTER shape and direction are set
        DB.WallUtils.DisallowWallJoinAtEnd(stem_wall, near_idx)
        
        return True
    except: return False

def execute_advanced_trim(doc, walls, pick_point):
    if len(walls) >= 2: return align_layered_wall(doc, walls[0], walls[1], pick_point, None)
    return False
