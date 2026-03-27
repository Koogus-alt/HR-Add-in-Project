# -*- coding: utf-8 -*-
from Autodesk.Revit.DB import *
import heerim_geom
import layered_trim_logic

def solve_2way(doc, uidoc, w1, w2, pt_picked, area_outline, log_func):
    """
    Handles standard 2-wall L and T junctions.
    Auto-Trim only (No UI).
    """
    if not w1.IsValidObject or not w2.IsValidObject: return False
    
    c1 = w1.Location.Curve
    c2 = w2.Location.Curve
    if not (isinstance(c1, Line) and isinstance(c2, Line)): return False
    
    inter = heerim_geom.get_intersection(c1, c2)
    if not isinstance(inter, XYZ): return False

    pt = inter
    j_type, stem_idx, cross_idx = heerim_geom.get_junction_info(c1, c2, pt)
    
    walls_pair = [w1, w2]

    if j_type == 'T':
        stem_geom = walls_pair[stem_idx]
        cross_geom = walls_pair[cross_idx]
        
        try:
            overlap, _, _ = layered_trim_logic.check_z_overlap(stem_geom, cross_geom)
            if not overlap:
                layered_trim_logic.match_wall_height(stem_geom, cross_geom)
        except: pass
        
        # Extend stem wall to centerline intersection and Allow Join
        c_stem = stem_geom.Location.Curve
        p0, p1 = c_stem.GetEndPoint(0), c_stem.GetEndPoint(1)
        if p0.DistanceTo(inter) < p1.DistanceTo(inter):
            stem_geom.Location.Curve = Line.CreateBound(inter, p1)
        else:
            stem_geom.Location.Curve = Line.CreateBound(p0, inter)
        
        try:
            doc.Regenerate()
            WallUtils.AllowWallJoinAtEnd(stem_geom, 0)
            WallUtils.AllowWallJoinAtEnd(stem_geom, 1)
            WallUtils.AllowWallJoinAtEnd(cross_geom, 0)
            WallUtils.AllowWallJoinAtEnd(cross_geom, 1)
            
            if JoinGeometryUtils.AreElementsJoined(doc, stem_geom, cross_geom):
                try: JoinGeometryUtils.UnjoinGeometry(doc, stem_geom, cross_geom)
                except: pass
            JoinGeometryUtils.JoinGeometry(doc, stem_geom, cross_geom)
        except: pass
        return True

    else:
        # L-JUNCTION or Corner
        def modify_end(w, c, p):
            p_s, p_e = c.GetEndPoint(0), c.GetEndPoint(1)
            if p_s.DistanceTo(p) < p_e.DistanceTo(p):
                w.Location.Curve = Line.CreateBound(p, p_e)
            else:
                w.Location.Curve = Line.CreateBound(p_s, p)

        if area_outline:
            for w_temp, c_temp in [(w1, c1), (w2, c2)]:
                p_s, p_e = c_temp.GetEndPoint(0), c_temp.GetEndPoint(1)
                near_p = p_s if p_s.DistanceTo(pt) < p_e.DistanceTo(pt) else p_e
                if heerim_geom.is_point_in_outline(near_p, area_outline):
                    modify_end(w_temp, c_temp, pt)
        else:
            modify_end(w1, c1, pt)
            modify_end(w2, c2, pt)
        
        try:
            doc.Regenerate()
            for w in [w1, w2]:
                WallUtils.AllowWallJoinAtEnd(w, 0); WallUtils.AllowWallJoinAtEnd(w, 1)
            if not JoinGeometryUtils.AreElementsJoined(doc, w1, w2):
                JoinGeometryUtils.JoinGeometry(doc, w1, w2)
        except: pass
        return True
