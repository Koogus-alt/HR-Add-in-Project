# -*- coding: utf-8 -*-
from Autodesk.Revit.DB import *
import heerim_geom
import solver_2way
import solver_3way
import solver_4way

def close_collinear_gaps(doc, walls, pt_picked, radius_or_outline, log_func):
    """
    Migrated shared logic to close gaps between collinear walls.
    radius_or_outline: radius(float) for Click, Outline for Drag.
    """
    log_func("Checking collinear gaps...")
    dirty = True; iteration = 0; limit = 5
    while dirty and iteration < limit:
        dirty = False; iteration += 1
        valid_walls = [w for w in walls if w.IsValidObject]
        for i in range(len(valid_walls)):
            if dirty: break
            for j in range(i + 1, len(valid_walls)):
                w1, w2 = valid_walls[i], valid_walls[j]
                c1, c2 = w1.Location.Curve, w2.Location.Curve
                if not (isinstance(c1, Line) and isinstance(c2, Line)): continue
                
                if heerim_geom.are_collinear(c1, c2):
                    pts1 = [c1.GetEndPoint(0), c1.GetEndPoint(1)]
                    pts2 = [c2.GetEndPoint(0), c2.GetEndPoint(1)]
                    min_dist = 1e9; gap_p1 = None; gap_p2 = None
                    for p_a in pts1:
                        for p_b in pts2:
                            dist = p_a.DistanceTo(p_b)
                            if dist < min_dist:
                                min_dist = dist; gap_p1 = p_a; gap_p2 = p_b
                    
                    if min_dist > 0.001:
                        mid_p = (gap_p1 + gap_p2) / 2.0
                        in_area = False
                        if isinstance(radius_or_outline, (float, int)):
                            in_area = mid_p.DistanceTo(pt_picked) <= radius_or_outline
                        else:
                            in_area = heerim_geom.is_point_in_outline(mid_p, radius_or_outline)
                        
                        if not in_area: continue

                        if w1.GetTypeId() == w2.GetTypeId():
                            try:
                                log_func("Merging collinear: {} + {}".format(w1.Id, w2.Id))
                                all_p = pts1 + pts2
                                dist_max = 0; far1 = None; far2 = None
                                for pa in all_p:
                                    for pb in all_p:
                                        d = pa.DistanceTo(pb)
                                        if d > dist_max: dist_max = d; far1, far2 = pa, pb
                                w1.Location.Curve = Line.CreateBound(far1, far2)
                                doc.Delete(w2.Id)
                                dirty = True
                            except: pass
                        else:
                            try:
                                log_func("Joining collinear: {} & {}".format(w1.Id, w2.Id))
                                for w, pts in [(w1, pts1), (w2, pts2)]:
                                    other = pts[1] if pts[0].DistanceTo(mid_p) < pts[1].DistanceTo(mid_p) else pts[0]
                                    w.Location.Curve = Line.CreateBound(mid_p, other)
                                doc.Regenerate()
                                if not JoinGeometryUtils.AreElementsJoined(doc, w1, w2):
                                    JoinGeometryUtils.JoinGeometry(doc, w1, w2)
                                dirty = True
                            except: pass
    return True

def process_trim(doc, uidoc, walls, pt_picked, area_outline, radius, log_func):
    """
    Main entry point for the trim process.
    """
    if not walls: return False
    
    area_key = area_outline if area_outline else radius
    close_collinear_gaps(doc, walls, pt_picked, area_key, log_func)
    
    active_walls = [w for w in walls if w.IsValidObject]
    
    if len(active_walls) == 2:
        return solver_2way.solve_2way(doc, uidoc, active_walls[0], active_walls[1], pt_picked, area_outline, log_func)
    
    elif len(active_walls) == 3:
        return solver_3way.solve_3way(doc, uidoc, active_walls, pt_picked, log_func)
    
    elif len(active_walls) == 4:
        return solver_4way.solve_4way(doc, uidoc, active_walls, pt_picked, log_func)
    
    elif len(active_walls) > 4:
        log_func("Multi-way junction (5+) currently processed via pairwise 2-way solvers.")
        success = False
        for i in range(len(active_walls)):
            for j in range(i + 1, len(active_walls)):
                if solver_2way.solve_2way(doc, uidoc, active_walls[i], active_walls[j], pt_picked, area_outline, log_func):
                    success = True
        return success

    return False
