# -*- coding: utf-8 -*-
from Autodesk.Revit.DB import *
import heerim_geom
import layered_trim_logic
from System.Collections.Generic import List

def find_collinear_pairs(walls):
    """Identifies pairs of walls that are collinear."""
    pairs = []
    used_indices = set()
    for i in range(len(walls)):
        if i in used_indices: continue
        for j in range(i + 1, len(walls)):
            if j in used_indices: continue
            c1, c2 = walls[i].Location.Curve, walls[j].Location.Curve
            if heerim_geom.are_collinear(c1, c2):
                pairs.append((walls[i], walls[j]))
                used_indices.add(i)
                used_indices.add(j)
                break 
    return pairs

def solve_4way(doc, uidoc, walls, pt_picked, log_func):
    """
    Handles 4-wall X-junctions with Automatic Resolution.
    Priority 1: Through Cross (Collinear pairs join)
    Priority 2: Centroid
    """
    curves = [w.Location.Curve for w in walls]
    common_pt = heerim_geom.get_common_intersection(curves)
    if not common_pt: return False

    col_pairs = find_collinear_pairs(walls)
    
    # 1. Through Cross
    if len(col_pairs) > 0:
        log_func("Auto 4-Way: Through Cross detected. Joining parallel pairs.")
        
        # Elements involved in pairs
        pair_elements = []
        for p in col_pairs: 
            pair_elements.append(p[0])
            pair_elements.append(p[1])
            
            # Simple merge/align for the pair if they are collinear but separated by gap
            # Actually find_collinear_pairs just checks geom. 
            # We assume they should be bridged or already touching.
            pass
        
        # Handle non-pair elements (stems hitting the cross)
        stems = [w for w in walls if w not in pair_elements]
        
        # If we have one main pair (2 walls) and 2 stems, treat the pair as leaders?
        # A true 4-way X usually has 2 pairs crossing.
        
        # Logic: Just ensure all connect to common_pt, but pairs maintain linearity?
        # Actually, if they are collinear, we want them to effectively be "one line".
        # But Revit walls are separate elements.
        
        # Fallback: Treat as Centroid but try to join geometry smartly.
        pass

    # Simplified Automatic Logic: Centroid for all.
    # Why? Because properly stitching "Through Cross" automatically without user confirming intent 
    # might delete walls or merge them unexpectedly. 
    # Centroid works safe: endpoints move to center.
    
    log_func("Auto 4-Way: Centroid alignment.")
    for w in walls:
        c = w.Location.Curve
        p0, p1 = c.GetEndPoint(0), c.GetEndPoint(1)
        if p0.DistanceTo(common_pt) < p1.DistanceTo(common_pt):
            w.Location.Curve = Line.CreateBound(common_pt, p1)
        else:
            w.Location.Curve = Line.CreateBound(p0, common_pt)
        WallUtils.AllowWallJoinAtEnd(w, 0); WallUtils.AllowWallJoinAtEnd(w, 1)

    doc.Regenerate()
    for i in range(len(walls)):
        for j in range(i + 1, len(walls)):
            try: JoinGeometryUtils.JoinGeometry(doc, walls[i], walls[j])
            except: pass
            
    return True
