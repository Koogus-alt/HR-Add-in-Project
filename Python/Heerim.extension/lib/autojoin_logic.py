# -*- coding: utf-8 -*-
from Autodesk.Revit.DB import *
from pyrevit import revit, DB

def get_elements_by_category(doc, category_name, scope_selection=None):
    """카테고리 이름으로 요소를 수집합니다."""
    collector = FilteredElementCollector(doc)
    if scope_selection:
        from System.Collections.Generic import List
        id_list = List[ElementId](scope_selection)
        collector = FilteredElementCollector(doc, id_list)
    else:
        collector = FilteredElementCollector(doc, doc.ActiveView.Id)

    # Class 기반 브로드매칭 (최신 Revit Floors 누락 방지)
    if category_name == "Floors":
        elements = list(collector.OfClass(Floor).WhereElementIsNotElementType().ToElements())
        return elements
    elif category_name == "Structural Foundations":
        elements = list(collector.OfClass(Floor).WhereElementIsNotElementType().ToElements())
        # 추가적으로 독립 기초(FamilyInstance 등) 캐치하려면 아래를 사용하지만 일단 Floor 호환성 유지
        collector = FilteredElementCollector(doc, doc.ActiveView.Id if not scope_selection else id_list)
        fnd = list(collector.OfCategory(BuiltInCategory.OST_StructuralFoundation).WhereElementIsNotElementType().ToElements())
        elements.extend(fnd)
        return list(set(elements))

    # Mapping for common names to built-in categories
    cat_map = {
        "Columns": BuiltInCategory.OST_Columns,
        "Structural Columns": BuiltInCategory.OST_StructuralColumns,
        "Structural Framing": BuiltInCategory.OST_StructuralFraming,
        "Structural Walls": BuiltInCategory.OST_Walls, # There is no OST_StructuralWalls in Revit
        "Walls": BuiltInCategory.OST_Walls,
        "Roofs": BuiltInCategory.OST_Roofs,
        "Generic Models": BuiltInCategory.OST_GenericModel,
        "Stairs": BuiltInCategory.OST_Stairs,
        "Topography/Toposolid": BuiltInCategory.OST_Topography,
        "Ramps": BuiltInCategory.OST_Ramps,
        "Ceilings": BuiltInCategory.OST_Ceilings
    }
    
    cat_enum = cat_map.get(category_name)
    if not cat_enum:
        return []

    elements = list(collector.OfCategory(cat_enum).WhereElementIsNotElementType().ToElements())
    
    # Structural Walls 필터링 로직 (일반 벽에서 구조 벽만 추출)
    if category_name == "Structural Walls":
        filtered = []
        for e in elements:
            p = e.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT)
            if p and p.AsInteger() == 1:
                filtered.append(e)
        return filtered
        
        
    return elements

def apply_auto_linework(doc, view, el1, el2):
    """Draws invisible detail lines over shared edges between el1 and el2 in 2D views."""
    # Only works in supported 2D views
    if view.ViewType not in [ViewType.FloorPlan, ViewType.CeilingPlan, ViewType.Section, ViewType.Elevation]:
        return 0
        
    invisible_style = None
    for gs in FilteredElementCollector(doc).OfClass(GraphicsStyle):
        if gs.GraphicsStyleCategory.Name in ["Invisible lines", "보이지 않는 선", "투명 선", "<Invisible lines>", "<보이지 않는 선>"]:
            invisible_style = gs
            break
            
    if not invisible_style:
        return 0
        
    lines_drawn = 0
    
    opt = Options()
    opt.View = view
    opt.ComputeReferences = True
    
    geom1 = el1.get_Geometry(opt)
    geom2 = el2.get_Geometry(opt)
    
    if not geom1 or not geom2:
        return 0
        
    # extract faces of el1
    faces1 = []
    for g1 in geom1:
        if isinstance(g1, Solid) and g1.Volume > 0:
            for f in g1.Faces: faces1.append(f)
        elif isinstance(g1, GeometryInstance):
            for gi in g1.GetInstanceGeometry():
                if isinstance(gi, Solid) and gi.Volume > 0:
                    for f in gi.Faces: faces1.append(f)
                    
    # extract edges of el2
    edges2 = []
    for g2 in geom2:
        if isinstance(g2, Solid) and g2.Volume > 0:
            for e in g2.Edges: edges2.append(e)
        elif isinstance(g2, GeometryInstance):
            for gi in g2.GetInstanceGeometry():
                if isinstance(gi, Solid) and gi.Volume > 0:
                    for e in gi.Edges: edges2.append(e)

    for edge in edges2:
        crv = edge.AsCurve()
        ep1 = crv.GetEndPoint(0)
        ep2 = crv.GetEndPoint(1)
        mid = crv.Evaluate(0.5, True)
        
        is_shared = False
        for face in faces1:
            try:
                res1 = face.Project(ep1)
                res2 = face.Project(ep2)
                resM = face.Project(mid)
                
                if res1 and res2 and resM:
                    if res1.Distance < 0.001 and res2.Distance < 0.001 and resM.Distance < 0.001:
                        is_shared = True
                        break
            except:
                pass
                
        if is_shared:
            # Flatten to view plane for plans
            if view.ViewType in [ViewType.FloorPlan, ViewType.CeilingPlan]:
                flat_crv = Line.CreateBound(XYZ(ep1.X, ep1.Y, view.Origin.Z), XYZ(ep2.X, ep2.Y, view.Origin.Z))
                if flat_crv.Length > 0.01:
                    try:
                        dc = doc.Create.NewDetailCurve(view, flat_crv)
                        dc.LineStyle = invisible_style
                        lines_drawn += 1
                    except: pass
            else:
                if crv.Length > 0.01:
                    try:
                        dc = doc.Create.NewDetailCurve(view, crv)
                        dc.LineStyle = invisible_style
                        lines_drawn += 1
                    except: pass
                    
    return sum([1 for _ in range(1)]) if lines_drawn > 0 else 0

def execute_auto_join(doc, priority_list, use_selection=False, is_unjoin=False, auto_linework=False):
    """
    priority_list: list of dicts [{"primary": "Columns", "secondary": "Floors", "invert": False}]
    """
    results = {"joined": 0, "unjoined": 0, "switched": 0, "errors": 0}
    
    selection_ids = None
    if use_selection:
        from pyrevit import revit
        selection_ids = revit.get_selection().element_ids
        if not selection_ids:
            return results

    import System
    for rule in priority_list:
        if isinstance(rule, dict):
            primary_cat = rule.get("primary")
            secondary_cat = rule.get("secondary")
            invert = rule.get("invert", False)
        else:
            primary_cat = getattr(rule, "primary", None)
            secondary_cat = getattr(rule, "secondary", None)
            invert = getattr(rule, "invert", False)

        if isinstance(invert, list) and len(invert) > 0:
            invert = invert[0]

        primary_elements = get_elements_by_category(doc, primary_cat, selection_ids)
        secondary_elements = get_elements_by_category(doc, secondary_cat, selection_ids)

        if not primary_elements or not secondary_elements:
            continue
            
        # 1. 모든 secondary 요소의 Id를 추출 (안전한 교집합 검사용)
        sec_ids_str = [s.Id.ToString() for s in secondary_elements]

        for p_el in primary_elements:
            bb = p_el.get_BoundingBox(None)
            if not bb: continue
            
            # 박스 0.5ft(약 150mm) 여유 확장
            offset = XYZ(0.5, 0.5, 0.5)
            outline = Outline(bb.Min - offset, bb.Max + offset)
            bb_filter = BoundingBoxIntersectsFilter(outline)
            
            # 현재 뷰에서 Bounding Box 안에 들어오는 모든 요소 수집
            collector = FilteredElementCollector(doc, doc.ActiveView.Id).WherePasses(bb_filter)
            
            for s_el in collector:
                # 찾아낸 요소가 Secondary_elements에 속한 놈인지 확인
                if s_el.Id.ToString() not in sec_ids_str:
                    continue
                    
                # 자기 자신이면 스킵
                if p_el.Id.ToString() == s_el.Id.ToString():
                    continue

                if "bb_matches" not in results: results["bb_matches"] = 0
                results["bb_matches"] += 1

                try:
                    is_joined = JoinGeometryUtils.AreElementsJoined(doc, p_el, s_el)
                    
                    if is_joined:
                        if "already_joined" not in results: results["already_joined"] = 0
                        results["already_joined"] += 1

                    if is_unjoin:
                        if is_joined:
                            try:
                                JoinGeometryUtils.UnjoinGeometry(doc, p_el, s_el)
                                results["unjoined"] += 1
                            except System.Exception: pass
                            except Exception: pass
                    else:
                        if not is_joined:
                            if "attempted_joins" not in results: results["attempted_joins"] = 0
                            results["attempted_joins"] += 1
                            try:
                                JoinGeometryUtils.JoinGeometry(doc, p_el, s_el)
                                results["joined"] += 1
                                is_joined = True
                            except System.Exception as ex: 
                                err_msg = str(ex)
                                if "cannot be joined" in err_msg:
                                    if "unjoinable" not in results: results["unjoinable"] = 0
                                    results["unjoinable"] += 1
                                else:
                                    if "error_msgs" not in results: results["error_msgs"] = []
                                    results["error_msgs"].append("Join Error (NET): " + err_msg.split('\n')[0])
                            except Exception as ex: 
                                if "error_msgs" not in results: results["error_msgs"] = []
                                results["error_msgs"].append("Join Error (PY): " + str(ex).split('\n')[0])
                        
                        if is_joined:
                            # 결합 순서 세팅
                            is_primary_cutting = JoinGeometryUtils.IsCuttingElementInJoin(doc, p_el, s_el)
                            target_primary_cutting = not invert
                            
                            if is_primary_cutting == target_primary_cutting:
                                if "correct_cut_count" not in results: results["correct_cut_count"] = 0
                                results["correct_cut_count"] += 1
                                
                            if is_primary_cutting != target_primary_cutting:
                                if "attempted_switches" not in results: results["attempted_switches"] = 0
                                results["attempted_switches"] += 1
                                try:
                                    JoinGeometryUtils.SwitchJoinOrder(doc, p_el, s_el)
                                    results["switched"] += 1
                                    
                                    # 진짜로 성공했는지 한 번 더 API로 확인
                                    new_state = JoinGeometryUtils.IsCuttingElementInJoin(doc, p_el, s_el)
                                    if new_state != target_primary_cutting:
                                        if "error_msgs" not in results: results["error_msgs"] = []
                                        results["error_msgs"].append("Switch Failed silently for {} (State unchanged)".format(p_el.Id))
                                        
                                except System.Exception as ex:
                                    if "error_msgs" not in results: results["error_msgs"] = []
                                    results["error_msgs"].append("Switch NET Error: " + str(ex))
                                except Exception as ex:
                                    if "error_msgs" not in results: results["error_msgs"] = []
                                    results["error_msgs"].append("Switch PY Error: " + str(ex))
                                    
                            if auto_linework:
                                try:
                                    drawn = apply_auto_linework(doc, doc.ActiveView, p_el, s_el)
                                    if drawn > 0:
                                        if "linework_edges" not in results: results["linework_edges"] = 0
                                        results["linework_edges"] += drawn
                                except Exception as e:
                                    if "error_msgs" not in results: results["error_msgs"] = []
                                    results["error_msgs"].append("Linework Error: " + str(e))
                except System.Exception as e:
                    results["errors"] += 1
                    if "error_msgs" not in results: results["error_msgs"] = []
                    results["error_msgs"].append("General NET Error: " + str(e))
                except Exception as e:
                    results["errors"] += 1
                    if "error_msgs" not in results: results["error_msgs"] = []
                    results["error_msgs"].append("General PY Error: " + str(e))
                    
    return results
