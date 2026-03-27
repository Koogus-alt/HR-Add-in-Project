# -*- coding: utf-8 -*-
from Autodesk.Revit.DB import XYZ, Line

TOLERANCE_PARALLEL = 0.01
TOLERANCE_ON_CURVE = 0.001

def get_intersection(curve1, curve2):
    """Calculate the intersection point of two lines."""
    p1 = curve1.GetEndPoint(0)
    q1 = curve1.GetEndPoint(1)
    p2 = curve2.GetEndPoint(0)
    q2 = curve2.GetEndPoint(1)
    
    x1, y1 = p1.X, p1.Y
    x2, y2 = q1.X, q1.Y
    x3, y3 = p2.X, p2.Y
    x4, y4 = q2.X, q2.Y
    
    denom = (y4 - y3) * (x2 - x1) - (x4 - x3) * (y2 - y1)
    if abs(denom) < 0.000001: return "PARALLEL"
    
    ua = ((x4 - x3) * (y1 - y3) - (y4 - y3) * (x1 - x3)) / denom
    x = x1 + ua * (x2 - x1)
    y = y1 + ua * (y2 - y1)
    return XYZ(x, y, p1.Z)

def are_collinear(c1, c2):
    """Check if two curves are collinear."""
    p1, q1 = c1.GetEndPoint(0), c1.GetEndPoint(1)
    p2, q2 = c2.GetEndPoint(0), c2.GetEndPoint(1)
    
    v1 = (q1 - p1).Normalize()
    v2_to_p2 = (p2 - p1).Normalize()
    
    if p1.IsAlmostEqualTo(p2): return True
    if v1.CrossProduct(v2_to_p2).GetLength() > TOLERANCE_PARALLEL: return False
    
    v2 = (q2 - p2).Normalize()
    if v1.CrossProduct(v2).GetLength() > TOLERANCE_PARALLEL: return False
    return True

def is_point_on_segment(pt, curve):
    """Check if a point lies on the line segment with tolerance."""
    p1 = curve.GetEndPoint(0)
    p2 = curve.GetEndPoint(1)
    min_x, max_x = min(p1.X, p2.X) - TOLERANCE_ON_CURVE, max(p1.X, p2.X) + TOLERANCE_ON_CURVE
    min_y, max_y = min(p1.Y, p2.Y) - TOLERANCE_ON_CURVE, max(p1.Y, p2.Y) + TOLERANCE_ON_CURVE
    return (min_x <= pt.X <= max_x and min_y <= pt.Y <= max_y)

def is_point_in_outline(pt, outline):
    """Check if a point is within a bounding box outline."""
    min_pt = outline.MinimumPoint
    max_pt = outline.MaximumPoint
    if not (min_pt.X <= pt.X <= max_pt.X): return False
    if not (min_pt.Y <= pt.Y <= max_pt.Y): return False
    return True

def get_junction_info(curve1, curve2, intersection_pt):
    """
    Categorizes the junction between two curves at a given intersection point.
    Returns: ('T', stem_idx, cross_idx) or ('L', None, None) or ('CROSS', None, None)
    """
    p1s, p1e = curve1.GetEndPoint(0), curve1.GetEndPoint(1)
    p2s, p2e = curve2.GetEndPoint(0), curve2.GetEndPoint(1)
    
    eps = 0.05 # 1.5cm tolerance
    
    # Check if intersection is on the "body" of the wall (not at endpoints)
    on_1_body = is_point_on_segment(intersection_pt, curve1) and intersection_pt.DistanceTo(p1s) > eps and intersection_pt.DistanceTo(p1e) > eps
    on_2_body = is_point_on_segment(intersection_pt, curve2) and intersection_pt.DistanceTo(p2s) > eps and intersection_pt.DistanceTo(p2e) > eps
    
    if on_1_body and on_2_body:
        return 'CROSS', None, None
    elif on_1_body:
        return 'T', 1, 0 # Curve 2 is stem, Curve 1 is crossbar
    elif on_2_body:
        return 'T', 0, 1 # Curve 1 is stem, Curve 2 is crossbar
    else:
        return 'L', None, None

def get_common_intersection(curves):
    """
    Calculate the average intersection point for a set of curves.
    Useful for resolving Y-junctions with 3+ walls.
    """
    if len(curves) < 2: return None
    
    pts = []
    for i in range(len(curves)):
        for j in range(i + 1, len(curves)):
            pt = get_intersection(curves[i], curves[j])
            if isinstance(pt, XYZ):
                pts.append(pt)
    
    if not pts: return None
    
    avg_x = sum(p.X for p in pts) / len(pts)
    avg_y = sum(p.Y for p in pts) / len(pts)
    avg_z = sum(p.Z for p in pts) / len(pts)
    
    return XYZ(avg_x, avg_y, avg_z)
