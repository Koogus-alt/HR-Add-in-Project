# -*- coding: utf-8 -*-
from Autodesk.Revit.DB import *
from Autodesk.Revit.UI import *
import heerim_geom
import math
from System.Collections.Generic import List

def get_h_mm(w):
    try:
        bb = w.get_BoundingBox(None)
        if bb:
            return bb.Max.Z / 0.00328084
        # Fallback to parameter if bbox fails
        h = w.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM).AsDouble()
        return h / 0.00328084
    except:
        return 0

def get_wall_width(w):
    try:
        return w.Width
    except:
        return w.WallType.Width

def solve_3way(doc, uidoc, walls, pt_picked, log_func):
    """
    Handles 3-wall junctions.
    Primarily checks for T-junctions (2 collinear walls + 1 stem wall).
    If found, the collinear walls are maintained/merged (if same type), and the stem is trimmed to them.
    If not, falls back to priority-based Y-junction logic.
    """
    if len(walls) != 3: return False
    
    # 1. Check for Collinear Pair (Continuous Wall broken by intersection)
    collinear_pair = None
    stem_wall = None
    
    for i in range(3):
        for j in range(i + 1, 3):
            c1 = walls[i].Location.Curve
            c2 = walls[j].Location.Curve
            if heerim_geom.are_collinear(c1, c2):
                collinear_pair = (walls[i], walls[j])
                stem_wall = [w for w in walls if w not in collinear_pair][0]
                break
        if collinear_pair: break
        
    if collinear_pair:
        log_func("Detected T-Junction: 2 Collinear walls + 1 Stem wall.")
        w1, w2 = collinear_pair
        
        # Merge collinear walls if they are the same type to form a single continuous cross wall
        main_wall = w1
        if w1.GetTypeId() == w2.GetTypeId():
            try:
                log_func("Merging collinear cross walls into single wall...")
                c1, c2 = w1.Location.Curve, w2.Location.Curve
                pts = [c1.GetEndPoint(0), c1.GetEndPoint(1), c2.GetEndPoint(0), c2.GetEndPoint(1)]
                max_d = 0; far_p1 = None; far_p2 = None
                for p_a in pts:
                    for p_b in pts:
                        d = p_a.DistanceTo(p_b)
                        if d > max_d:
                            max_d = d; far_p1 = p_a; far_p2 = p_b
                
                w1.Location.Curve = Line.CreateBound(far_p1, far_p2)
                doc.Delete(w2.Id)
                # Now we have a simple 2-way T-junction
                import solver_2way
                return solver_2way.solve_2way(doc, uidoc, main_wall, stem_wall, pt_picked, None, log_func)
            except Exception as e:
                log_func("Collinear merge failed: " + str(e))
                # Fallback to standard 3-way Y logic if merge fails
                pass
        else:
            log_func("Collinear walls are different types. Extending to meet point.")
            # Join both to the stem point
            c_stem = stem_wall.Location.Curve
            inter1 = heerim_geom.get_intersection(w1.Location.Curve, c_stem)
            inter2 = heerim_geom.get_intersection(w2.Location.Curve, c_stem)
            target_pt = inter1 if isinstance(inter1, XYZ) else inter2 
            
            if not isinstance(target_pt, XYZ):
                target_pt = pt_picked # Fallback to click/drag center
                
            for w in walls:
                c = w.Location.Curve
                p0, p1 = c.GetEndPoint(0), c.GetEndPoint(1)
                far_pt = p1 if p0.DistanceTo(target_pt) < p1.DistanceTo(target_pt) else p0
                w.Location.Curve = Line.CreateBound(target_pt, far_pt)
                WallUtils.AllowWallJoinAtEnd(w, 0); WallUtils.AllowWallJoinAtEnd(w, 1)
            
            doc.Regenerate()
            try:
                JoinGeometryUtils.JoinGeometry(doc, w1, w2)
                JoinGeometryUtils.JoinGeometry(doc, w1, stem_wall)
                JoinGeometryUtils.JoinGeometry(doc, w2, stem_wall)
            except: pass
            return True

    # 2. Y-Junction Fallback Logic (Non-collinear)
    log_func("No Collinear walls found. Standard Y-Junction logic applied.")
    curr_curves = [w.Location.Curve for w in walls]
    common_pt = heerim_geom.get_common_intersection(curr_curves)
    if not common_pt:
        common_pt = pt_picked
        
    for w in walls:
        try:
            c = w.Location.Curve
            p0, p1 = c.GetEndPoint(0), c.GetEndPoint(1)
            far_pt = p1 if p0.DistanceTo(common_pt) < p1.DistanceTo(common_pt) else p0
            
            # Draw all walls tightly to the common intersection point
            w.Location.Curve = Line.CreateBound(common_pt, far_pt)
            WallUtils.AllowWallJoinAtEnd(w, 0); WallUtils.AllowWallJoinAtEnd(w, 1)
        except: pass
            
    doc.Regenerate()
    for i in range(len(walls)):
        for j in range(i + 1, len(walls)):
            try:
                if not JoinGeometryUtils.AreElementsJoined(doc, walls[i], walls[j]):
                    JoinGeometryUtils.JoinGeometry(doc, walls[i], walls[j])
            except: pass
            
    return True
