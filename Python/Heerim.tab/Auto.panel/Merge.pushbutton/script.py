# -*- coding: utf-8 -*-
import os
import sys
import json
import math
import datetime

from Autodesk.Revit.DB import *
from Autodesk.Revit.UI import *
from Autodesk.Revit.UI.Selection import *
from Autodesk.Revit.DB.Analysis import *
from pyrevit import revit, DB, UI, script, forms

import clr
clr.AddReference("PresentationFramework")
clr.AddReference("System.Drawing")
import System
from System.Windows import Application, Window, WindowStyle, Controls, Media, Shapes
from System.Windows.Interop import WindowInteropHelper
from System.Windows.Threading import DispatcherTimer
import System.Windows.Forms as Forms
import ctypes

__title__ = "AutoMerge"

# --- Load Shared Library (Fixed Path to Extension Root) ---
LIB_PATH = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "..", "lib"))
if LIB_PATH not in sys.path:
    sys.path.append(LIB_PATH)
import heerim_geom
import layered_trim_logic
import autotrim_engine

import solver_2way
import solver_3way
try:
    from importlib import reload
except ImportError:
    pass
reload(heerim_geom)
reload(layered_trim_logic)
reload(solver_2way)
reload(solver_3way)
reload(autotrim_engine)

doc = revit.doc
uidoc = revit.uidoc

# Path to current folder (pushbutton) to find assets locally
PANEL_DIR = os.path.dirname(__file__)

LOG_FILE = os.path.join(PANEL_DIR, "autotrim.log")

def log(msg, mode_prefix="[MERGE]"):
    pass # Disabled file logging for absolute maximum performance

def load_history():
    pass # Disabling output window history logging for performance

# --- UI Config Load/Save ---
CONFIG_FILE = os.path.join(PANEL_DIR, "config.json")
def load_config():
    default_config = {"search_radius_mm": 3000, "search_radius_ft": 9.84}
    if os.path.exists(CONFIG_FILE):
        try:
            with open(CONFIG_FILE, "r") as f:
                return json.load(f)
        except: pass
    return default_config

def save_config(config):
    try:
        with open(CONFIG_FILE, "w") as f:
            json.dump(config, f, indent=4)
    except: pass

# --- Helper Functions ---
def create_marker_filled_square(pt, size=0.3):
    try:
        p_min = XYZ(pt.X - size, pt.Y - size, pt.Z - size)
        p_max = XYZ(pt.X + size, pt.Y + size, pt.Z + size)
        p1, p2, p3, p4 = XYZ(p_min.X, p_min.Y, p_min.Z), XYZ(p_max.X, p_min.Y, p_min.Z), XYZ(p_max.X, p_max.Y, p_min.Z), XYZ(p_min.X, p_max.Y, p_min.Z)
        loop = CurveLoop()
        loop.Append(Line.CreateBound(p1, p2))
        loop.Append(Line.CreateBound(p2, p3))
        loop.Append(Line.CreateBound(p3, p4))
        loop.Append(Line.CreateBound(p4, p1))
        
        from System.Collections.Generic import List
        solid = GeometryCreationUtilities.CreateExtrusionGeometry(List[CurveLoop]([loop]), XYZ.BasisZ, size * 2)
        ds = DirectShape.CreateElement(doc, ElementId(BuiltInCategory.OST_GenericModel))
        ds.SetShape([solid])
        return ds.Id
    except: return None

def create_radius_circle(center_pt, radius_ft):
    try:
        num_segments = 64
        points = []
        for i in range(num_segments):
            angle = (2.0 * math.pi * i) / num_segments
            points.append(XYZ(center_pt.X + radius_ft * math.cos(angle), center_pt.Y + radius_ft * math.sin(angle), center_pt.Z))
        loop = CurveLoop()
        for i in range(num_segments):
            loop.Append(Line.CreateBound(points[i], points[(i + 1) % num_segments]))
        from System.Collections.Generic import List
        solid = GeometryCreationUtilities.CreateExtrusionGeometry(List[CurveLoop]([loop]), XYZ.BasisZ, 0.05)
        ds = DirectShape.CreateElement(doc, ElementId(BuiltInCategory.OST_GenericModel))
        ds.SetShape([solid])
        return ds.Id
    except: return None

def create_selection_box_graphics(pick_box):
    try:
        p1, p2 = pick_box.Min, pick_box.Max
        x_min, x_max = min(p1.X, p2.X), max(p1.X, p2.X)
        y_min, y_max = min(p1.Y, p2.Y), max(p1.Y, p2.Y)
        z = p1.Z
        pta, ptb, ptc, ptd = XYZ(x_min, y_min, z), XYZ(x_max, y_min, z), XYZ(x_max, y_max, z), XYZ(x_min, y_max, z)
        loop = CurveLoop()
        loop.Append(Line.CreateBound(pta, ptb))
        loop.Append(Line.CreateBound(ptb, ptc))
        loop.Append(Line.CreateBound(ptc, ptd))
        loop.Append(Line.CreateBound(ptd, pta))
        from System.Collections.Generic import List
        solid = GeometryCreationUtilities.CreateExtrusionGeometry(List[CurveLoop]([loop]), XYZ.BasisZ, 0.05)
        ds = DirectShape.CreateElement(doc, ElementId(BuiltInCategory.OST_GenericModel))
        ds.SetShape([solid])
        return ds.Id
    except: return None

# --- Visual Overlay for Mouse Tracking ---
class MouseOverlay(Window):
    def __init__(self, radius_mm):
        super(MouseOverlay, self).__init__()
        self.radius_mm = radius_mm
        self.WindowStyle = WindowStyle.None
        self.AllowsTransparency = True
        self.ShowActivated = False
        self.Focusable = False
        self.Background = Media.Brushes.Transparent
        self.Topmost = True
        self.ShowInTaskbar = False
        self.IsHitTestVisible = False # Allows clicking through the overlay
        
        # UI Element: Circle
        self.canvas = Controls.Canvas()
        self.Content = self.canvas
        
        self.circle = Shapes.Ellipse()
        self.circle.Stroke = Media.BrushConverter().ConvertFromString("#8034C759") # Semi-transparent Green
        self.circle.StrokeThickness = 2
        self.circle.Fill = Media.BrushConverter().ConvertFromString("#2034C759") # Very transparent Green
        
        self.canvas.Children.Add(self.circle)
        
        # Timer for updating position
        self.timer = DispatcherTimer()
        self.timer.Interval = System.TimeSpan.FromMilliseconds(16) # ~60fps
        self.timer.Tick += self.update_position
        
    def show_overlay(self):
        self.Show()
        try:
            # Set Win32 Styles for Click-through and No-Activate
            hwnd = WindowInteropHelper(self).Handle
            GWL_EXSTYLE = -20
            WS_EX_LAYERED = 0x80000
            WS_EX_TRANSPARENT = 0x20
            WS_EX_NOACTIVATE = 0x08000000
            
            style = ctypes.windll.user32.GetWindowLongW(hwnd, GWL_EXSTYLE)
            ctypes.windll.user32.SetWindowLongW(hwnd, GWL_EXSTYLE, style | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE)
        except: pass
        self.timer.Start()
        
    def hide_overlay(self):
        self.timer.Stop()
        self.Hide()
        
    def update_position(self, sender, e):
        try:
            # 1. Get Mouse Position in Screen Coordinates
            pos = Forms.Cursor.Position
            
            # 2. Get Revit UIView to calculate scale
            active_view = doc.ActiveView
            ui_views = uidoc.GetOpenUIViews()
            current_ui_view = next((v for v in ui_views if v.ViewId == active_view.Id), None)
            
            if not current_ui_view:
                return
            
            # 3. Calculate Scale (mm to pixels)
            # Revit uses feet internally. radius_ft is what we have.
            # We need to find how many pixels represent 1 foot in the current view.
            rect = current_ui_view.GetWindowRectangle() # Screen pixels
            zoom_corners = current_ui_view.GetZoomCorners() # Revit coordinates (XYZ)
            
            view_width_ft = abs(zoom_corners[0].X - zoom_corners[1].X)
            pix_width = abs(rect.Right - rect.Left)
            
            if view_width_ft == 0: return
            
            pixels_per_foot = pix_width / view_width_ft
            radius_ft = self.radius_mm * 0.00328084
            radius_px = radius_ft * pixels_per_foot
            
            # 4. Update Overlay Window Position and Size
            self.Width = radius_px * 2 + 10
            self.Height = radius_px * 2 + 10
            self.Left = pos.X - radius_px - 5
            self.Top = pos.Y - radius_px - 5
            
            # 5. Update Circle Size
            self.circle.Width = radius_px * 2
            self.circle.Height = radius_px * 2
            Controls.Canvas.SetLeft(self.circle, 5)
            Controls.Canvas.SetTop(self.circle, 5)
            
        except Exception as ex:
            # Silently fail to avoid crashing the main loop
            pass

# --- Logic: Click Mode ---
def run_click_mode(radius_ft, radius_mm):
    session_count = 0
    log("=== Auto Merge Click Mode Started (Press ESC to exit) ===", "[CLICK]")
    log("Search Radius: {:.0f}mm ({:.2f}ft)".format(radius_mm, radius_ft), "[CLICK]")

    while True:
        try:
            load_history()
            session_count += 1
            log("--- Session #{} ---".format(session_count), "[CLICK]")
            
            prompt_msg = "Click near walls to merge (Radius: {:.0f}mm / {:.2f}ft, ESC to exit)".format(radius_mm, radius_ft)
            
            # Show Mouse Following Overlay
            overlay = MouseOverlay(radius_mm)
            overlay.show_overlay()
            
            try:
                pt_picked = uidoc.Selection.PickPoint(prompt_msg)
            finally:
                overlay.hide_overlay()
                overlay.Close()

            log("Clicked at: {}".format(pt_picked), "[CLICK]")
            
            min_pt = XYZ(pt_picked.X - radius_ft, pt_picked.Y - radius_ft, pt_picked.Z - 10)
            max_pt = XYZ(pt_picked.X + radius_ft, pt_picked.Y + radius_ft, pt_picked.Z + 10)
            outline = Outline(min_pt, max_pt)
            bb_filter = BoundingBoxIntersectsFilter(outline)
            
            walls_candidate = list(FilteredElementCollector(doc, doc.ActiveView.Id).OfClass(Wall).WherePasses(bb_filter).ToElements())
            
            walls = []
            for w in walls_candidate:
                 c = w.Location.Curve
                 if isinstance(c, Line):
                     dist1 = c.GetEndPoint(0).DistanceTo(pt_picked)
                     dist2 = c.GetEndPoint(1).DistanceTo(pt_picked)
                     is_near = dist1 <= radius_ft or dist2 <= radius_ft
                     if not is_near:
                         proj_result = c.Project(pt_picked)
                         if proj_result:
                             proj_pt = proj_result.XYZPoint
                             if heerim_geom.is_point_on_segment(proj_pt, c) and proj_pt.DistanceTo(pt_picked) <= radius_ft:
                                 is_near = True
                     if is_near: walls.append(w)
            
            tg = TransactionGroup(doc, "Click Merge Preview")
            tg.Start()
            t_preview = Transaction(doc, "Show Preview Markers")
            t_preview.Start()
            
            # Render radius circle and center and set their graphic overrides FIRST so they show even if no walls are found
            circle_id = create_radius_circle(pt_picked, radius_ft)
            
            cp1 = Line.CreateBound(pt_picked + XYZ(0.3, 0, 0), pt_picked - XYZ(0.3, 0, 0))
            cp2 = Line.CreateBound(pt_picked + XYZ(0, 0.3, 0), pt_picked - XYZ(0, 0.3, 0))
            ds_center = DirectShape.CreateElement(doc, ElementId(BuiltInCategory.OST_GenericModel))
            ds_center.SetShape([cp1, cp2])
            center_id = ds_center.Id
            
            # Apply overrides for the circle
            ogs_circle = OverrideGraphicSettings()
            ogs_circle.SetProjectionLineColor(Color(0, 150, 0))
            patterns = FilteredElementCollector(doc).OfClass(FillPatternElement).ToElements()
            solid_pat = next((p for p in patterns if p.GetFillPattern().IsSolidFill), None)
            if solid_pat: ogs_circle.SetSurfaceForegroundPatternId(solid_pat.Id)
            ogs_circle.SetSurfaceForegroundPatternColor(Color(0, 150, 0))
            ogs_circle.SetSurfaceTransparency(85)
            if circle_id:
                try: doc.ActiveView.SetElementOverrides(circle_id, ogs_circle)
                except: pass
                
            # Apply overrides for the center cross
            ogs_center = OverrideGraphicSettings()
            ogs_center.SetProjectionLineColor(Color(255, 128, 0))
            ogs_center.SetProjectionLineWeight(4)
            if center_id:
                try: doc.ActiveView.SetElementOverrides(center_id, ogs_center)
                except: pass

            if not walls:
                t_preview.Commit() # Commit early to show circle first
                uidoc.RefreshActiveView()
                forms.alert("반경 내에 병합할 부재(벽 등)가 없습니다.\n다른 곳을 클릭하거나 반경을 늘려보세요.", title="Click Merge")
                log("No elements found within {:.0f}mm.".format(radius_mm), "[CLICK]")
                # Delete the preview markers
                t_delete = Transaction(doc, "Delete Preview")
                t_delete.Start()
                if circle_id: doc.Delete(circle_id)
                if center_id: doc.Delete(center_id)
                t_delete.Commit()
                uidoc.RefreshActiveView()
                tg.RollBack()
                continue
            elif len(walls) < 2:
                t_preview.Commit() # Commit early to show circle first
                uidoc.RefreshActiveView()
                forms.alert("병합을 하려면 최소 2개의 부재가 반경 내에 있어야 합니다.\n(현재 {}개 발견)".format(len(walls)), title="Click Merge")
                log("Need at least 2 walls to merge. Found: {}".format(len(walls)), "[CLICK]")
                # Delete the preview markers
                t_delete = Transaction(doc, "Delete Preview")
                t_delete.Start()
                if circle_id: doc.Delete(circle_id)
                if center_id: doc.Delete(center_id)
                t_delete.Commit()
                uidoc.RefreshActiveView()
                tg.RollBack()
                continue
            
            log("Processing {} walls...".format(len(walls)), "[CLICK]")
            
            preview_points = []
            for w in walls:
                c = w.Location.Curve
                if c.GetEndPoint(0).DistanceTo(pt_picked) <= radius_ft: preview_points.append((c.GetEndPoint(0), w.Id))
                if c.GetEndPoint(1).DistanceTo(pt_picked) <= radius_ft: preview_points.append((c.GetEndPoint(1), w.Id))
            
            marker_ids = []
            for pt, wid in preview_points:
                mid = create_marker_filled_square(pt, size=0.2)
                if mid: marker_ids.append(mid)
            
            target_ids = []
            if len(walls) >= 2:
                junctions = []
                def dist_2d(p1, p2): return math.sqrt((p1.X - p2.X)**2 + (p1.Y - p2.Y)**2)
                for i in range(len(walls)):
                    for j in range(i + 1, len(walls)):
                        w1, w2 = walls[i], walls[j]
                        c1, c2 = w1.Location.Curve, w2.Location.Curve
                        
                        l1_2d = Line.CreateBound(XYZ(c1.GetEndPoint(0).X, c1.GetEndPoint(0).Y, 0), XYZ(c1.GetEndPoint(1).X, c1.GetEndPoint(1).Y, 0))
                        l2_2d = Line.CreateBound(XYZ(c2.GetEndPoint(0).X, c2.GetEndPoint(0).Y, 0), XYZ(c2.GetEndPoint(1).X, c2.GetEndPoint(1).Y, 0))
                        
                        inter_results = l1_2d.Intersect(l2_2d)
                        inter = None
                        if inter_results != SetComparisonResult.Overlap:
                             x1, y1 = l1_2d.GetEndPoint(0).X, l1_2d.GetEndPoint(0).Y
                             x2, y2 = l1_2d.GetEndPoint(1).X, l1_2d.GetEndPoint(1).Y
                             x3, y3 = l2_2d.GetEndPoint(0).X, l2_2d.GetEndPoint(0).Y
                             x4, y4 = l2_2d.GetEndPoint(1).X, l2_2d.GetEndPoint(1).Y
                             denom = (y4 - y3) * (x2 - x1) - (x4 - x3) * (y2 - y1)
                             if abs(denom) > 1e-9:
                                 ua = ((x4 - x3) * (y1 - y3) - (y4 - y3) * (x1 - x3)) / denom
                                 inter = XYZ(x1 + ua * (x2 - x1), y1 + ua * (y2 - y1), pt_picked.Z)
                        
                        show_junction = False
                        if inter:
                            if dist_2d(inter, pt_picked) <= radius_ft: show_junction = True
                            else:
                                p1_close = c1.GetEndPoint(0) if dist_2d(c1.GetEndPoint(0), inter) < dist_2d(c1.GetEndPoint(1), inter) else c1.GetEndPoint(1)
                                p2_close = c2.GetEndPoint(0) if dist_2d(c2.GetEndPoint(0), inter) < dist_2d(c2.GetEndPoint(1), inter) else c2.GetEndPoint(1)
                                if dist_2d(p1_close, pt_picked) <= radius_ft or dist_2d(p2_close, pt_picked) <= radius_ft:
                                    show_junction = True

                        if show_junction and inter: junctions.append((inter, [w1.Id, w2.Id]))
                
                for i in range(len(walls)):
                    for j in range(i + 1, len(walls)):
                        w1, w2 = walls[i], walls[j]
                        c1, c2 = w1.Location.Curve, w2.Location.Curve
                        if heerim_geom.are_collinear(c1, c2):
                            pts = [c1.GetEndPoint(0), c1.GetEndPoint(1), c2.GetEndPoint(0), c2.GetEndPoint(1)]
                            d_min = 1e9; best_m = None
                            for pa in [pts[0], pts[1]]:
                                for pb in [pts[2], pts[3]]:
                                    d = pa.DistanceTo(pb)
                                    if d < d_min: d_min = d; best_m = (pa + pb) / 2.0
                            if best_m and best_m.DistanceTo(pt_picked) <= radius_ft:
                                junctions.append((best_m, [w1.Id, w2.Id]))

                for target_pt, involved_ids in junctions:
                    tid = create_marker_filled_square(target_pt, size=0.15)
                    if tid: target_ids.append(tid)
                    lines = []
                    for pt, wid in preview_points:
                        if wid in involved_ids and pt.DistanceTo(target_pt) > 0.01:
                            lines.append(Line.CreateBound(pt, target_pt))
                    if lines:
                        ds_conn = DirectShape.CreateElement(doc, ElementId(BuiltInCategory.OST_GenericModel))
                        ds_conn.SetShape(lines)
                        target_ids.append(ds_conn.Id)

            ogs_transparent = OverrideGraphicSettings()
            ogs_transparent.SetSurfaceTransparency(70)
            for w in walls:
                try: doc.ActiveView.SetElementOverrides(w.Id, ogs_transparent)
                except: pass
            
            ogs_red = OverrideGraphicSettings()
            ogs_red.SetSurfaceForegroundPatternColor(Color(255, 0, 0))
            if solid_pat: ogs_red.SetSurfaceForegroundPatternId(solid_pat.Id)
            for mid in marker_ids:
                try: doc.ActiveView.SetElementOverrides(mid, ogs_red)
                except: pass
            
            ogs_blue = OverrideGraphicSettings()
            ogs_blue.SetProjectionLineColor(Color(0, 112, 192))
            ogs_blue.SetProjectionLineWeight(5)
            ogs_blue_filled = OverrideGraphicSettings()
            ogs_blue_filled.SetSurfaceForegroundPatternColor(Color(0, 112, 192))
            if solid_pat: ogs_blue_filled.SetSurfaceForegroundPatternId(solid_pat.Id)
            
            for tid in target_ids:
                try: 
                    doc.ActiveView.SetElementOverrides(tid, ogs_blue)
                    doc.ActiveView.SetElementOverrides(tid, ogs_blue_filled) 
                except: pass
            
            t_preview.Commit()
            uidoc.RefreshActiveView()
            
            res = TaskDialog.Show("Click Merge", "Previewing Endpoints.\nProceed to Merge?", TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No)
            tg.RollBack()
            
            if res != TaskDialogResult.Yes:
                log("Cancelled by user. Skipping to next selection...", "[CLICK]")
                continue
            else:
                log("Proceeding...", "[CLICK]")
                t = Transaction(doc, "Heerim Click Merge")
                t.Start()
                autotrim_engine.process_trim(doc, uidoc, walls, pt_picked, None, radius_ft, lambda msg: log(msg, "[CLICK]"))
                t.Commit()
                uidoc.RefreshActiveView()
                log("Session #{} completed. Ready for next selection...".format(session_count), "[CLICK]")
    
        except Exception as e:
            break

# --- Logic: Drag Mode ---
def run_drag_mode():
    session_count = 0
    log("=== Auto Merge Drag Mode Started (Press ESC to exit) ===", "[DRAG]")

    while True:
        try:
            load_history()
            session_count += 1
            log("--- Session #{} ---".format(session_count), "[DRAG]")
            
            # Change prompt to indicate dashed box (Revit natively uses dashed for Crossing, solid for Enclosing)
            pick_box = uidoc.Selection.PickBox(PickBoxStyle.Directional, "Drag to select area (ESC to exit)")
            
            p1 = pick_box.Min; p2 = pick_box.Max
            x_min, y_min, z_min = min(p1.X, p2.X), min(p1.Y, p2.Y), min(p1.Z, p2.Z) - 100
            x_max, y_max, z_max = max(p1.X, p2.X), max(p1.Y, p2.Y), max(p1.Z, p2.Z) + 100
            outline = Outline(XYZ(x_min, y_min, z_min), XYZ(x_max, y_max, z_max))
            bb_filter = BoundingBoxIntersectsFilter(outline)
            
            walls_candidate = list(FilteredElementCollector(doc, doc.ActiveView.Id).OfClass(Wall).WherePasses(bb_filter).ToElements())
            
            # Additional check to ensure walls intersect the 2D bounding box
            walls = []
            for w in walls_candidate:
                c = w.Location.Curve
                if c:
                    if heerim_geom.is_point_in_outline(c.GetEndPoint(0), outline) or heerim_geom.is_point_in_outline(c.GetEndPoint(1), outline):
                        walls.append(w)
                    else:
                        # Check intersection
                        l1_2d = Line.CreateBound(XYZ(c.GetEndPoint(0).X, c.GetEndPoint(0).Y, 0), XYZ(c.GetEndPoint(1).X, c.GetEndPoint(1).Y, 0))
                        box_lines = [
                            Line.CreateBound(XYZ(x_min, y_min, 0), XYZ(x_max, y_min, 0)),
                            Line.CreateBound(XYZ(x_max, y_min, 0), XYZ(x_max, y_max, 0)),
                            Line.CreateBound(XYZ(x_max, y_max, 0), XYZ(x_min, y_max, 0)),
                            Line.CreateBound(XYZ(x_min, y_max, 0), XYZ(x_min, y_min, 0))
                        ]
                        for bl in box_lines:
                            if l1_2d.Intersect(bl) == SetComparisonResult.Overlap:
                                walls.append(w)
                                break
            
            tg = TransactionGroup(doc, "Auto Merge Preview")
            tg.Start()
        
            box_center = (outline.MinimumPoint + outline.MaximumPoint) / 2.0
            t_preview = Transaction(doc, "Highlight Preview")
            t_preview.Start()
            
            box_id = create_selection_box_graphics(pick_box)
            
            cp1 = Line.CreateBound(box_center + XYZ(0.3, 0, 0), box_center - XYZ(0.3, 0, 0))
            cp2 = Line.CreateBound(box_center + XYZ(0, 0.3, 0), box_center - XYZ(0, 0.3, 0))
            ds_center = DirectShape.CreateElement(doc, ElementId(BuiltInCategory.OST_GenericModel))
            ds_center.SetShape([cp1, cp2])
            center_id = ds_center.Id
            
            ogs_box = None
            patterns = FilteredElementCollector(doc).OfClass(FillPatternElement).ToElements()
            solid_pat = next((p for p in patterns if p.GetFillPattern().IsSolidFill), None)
            
            if box_id:
                ogs_box = OverrideGraphicSettings()
                ogs_box.SetProjectionLineColor(Color(0, 150, 0))
                if solid_pat: ogs_box.SetSurfaceForegroundPatternId(solid_pat.Id)
                ogs_box.SetSurfaceForegroundPatternColor(Color(0, 150, 0))
                ogs_box.SetSurfaceTransparency(85)
                try: doc.ActiveView.SetElementOverrides(box_id, ogs_box)
                except: pass
                
            if center_id:
                ogs_center = OverrideGraphicSettings()
                ogs_center.SetProjectionLineColor(Color(255, 128, 0))
                ogs_center.SetProjectionLineWeight(4)
                try: doc.ActiveView.SetElementOverrides(center_id, ogs_center)
                except: pass

            if not walls:
                t_preview.Commit() # Show empty box
                uidoc.RefreshActiveView()
                forms.alert("선택 영역 내에 병합할 부재(벽 등)가 없습니다.\n선택 영역을 다시 지정해주세요.", title="Auto Merge")
                log("No Elements Selected.", "[DRAG]")
                t_delete = Transaction(doc, "Delete Preview")
                t_delete.Start()
                if box_id: doc.Delete(box_id)
                if center_id: doc.Delete(center_id)
                t_delete.Commit()
                uidoc.RefreshActiveView()
                tg.RollBack()
                continue
            elif len(walls) < 2:
                t_preview.Commit() # Show box
                uidoc.RefreshActiveView()
                forms.alert("병합을 하려면 최소 2개의 부재가 선택 영역에 포함되어야 합니다.\n(현재 {}개 포함)".format(len(walls)), title="Auto Merge")
                log("Need at least 2 walls to merge. Found: {}".format(len(walls)), "[DRAG]")
                t_delete = Transaction(doc, "Delete Preview")
                t_delete.Start()
                if box_id: doc.Delete(box_id)
                if center_id: doc.Delete(center_id)
                t_delete.Commit()
                uidoc.RefreshActiveView()
                tg.RollBack()
                continue

            log("Selected: {} Walls.".format(len(walls)), "[DRAG]")
            
            cp1 = Line.CreateBound(box_center + XYZ(0.3, 0, 0), box_center - XYZ(0.3, 0, 0))
            cp2 = Line.CreateBound(box_center + XYZ(0, 0.3, 0), box_center - XYZ(0, 0.3, 0))
            ds_center = DirectShape.CreateElement(doc, ElementId(BuiltInCategory.OST_GenericModel))
            ds_center.SetShape([cp1, cp2])
            center_id = ds_center.Id

            points_to_show = []
            for w in walls:
                c = w.Location.Curve
                if heerim_geom.is_point_in_outline(c.GetEndPoint(0), outline): points_to_show.append((c.GetEndPoint(0), w.Id))
                if heerim_geom.is_point_in_outline(c.GetEndPoint(1), outline): points_to_show.append((c.GetEndPoint(1), w.Id))
            
            marker_ids = []
            for pt, wid in points_to_show:
                mid = create_marker_filled_square(pt, size=0.25)
                if mid: marker_ids.append(mid)
            
            target_ids = []
            if len(walls) >= 2:
                junctions = []
                def dist_2d(p1, p2): return math.sqrt((p1.X - p2.X)**2 + (p1.Y - p2.Y)**2)
                for i in range(len(walls)):
                    for j in range(i + 1, len(walls)):
                        w1, w2 = walls[i], walls[j]
                        c1, c2 = w1.Location.Curve, w2.Location.Curve
                        
                        l1_2d = Line.CreateBound(XYZ(c1.GetEndPoint(0).X, c1.GetEndPoint(0).Y, 0), XYZ(c1.GetEndPoint(1).X, c1.GetEndPoint(1).Y, 0))
                        l2_2d = Line.CreateBound(XYZ(c2.GetEndPoint(0).X, c2.GetEndPoint(0).Y, 0), XYZ(c2.GetEndPoint(1).X, c2.GetEndPoint(1).Y, 0))
                        
                        inter_results = l1_2d.Intersect(l2_2d)
                        inter = None
                        if inter_results != SetComparisonResult.Overlap:
                             x1, y1 = l1_2d.GetEndPoint(0).X, l1_2d.GetEndPoint(0).Y
                             x2, y2 = l1_2d.GetEndPoint(1).X, l1_2d.GetEndPoint(1).Y
                             x3, y3 = l2_2d.GetEndPoint(0).X, l2_2d.GetEndPoint(0).Y
                             x4, y4 = l2_2d.GetEndPoint(1).X, l2_2d.GetEndPoint(1).Y
                             denom = (y4 - y3) * (x2 - x1) - (x4 - x3) * (y2 - y1)
                             if abs(denom) > 1e-9:
                                 ua = ((x4 - x3) * (y1 - y3) - (y4 - y3) * (x1 - x3)) / denom
                                 inter = XYZ(x1 + ua * (x2 - x1), y1 + ua * (y2 - y1), box_center.Z)
                        
                        show_junction = False
                        if inter:
                            if heerim_geom.is_point_in_outline(inter, outline):
                                show_junction = True
                            else:
                                p1_close = c1.GetEndPoint(0) if dist_2d(c1.GetEndPoint(0), inter) < dist_2d(c1.GetEndPoint(1), inter) else c1.GetEndPoint(1)
                                p2_close = c2.GetEndPoint(0) if dist_2d(c2.GetEndPoint(0), inter) < dist_2d(c2.GetEndPoint(1), inter) else c2.GetEndPoint(1)
                                if heerim_geom.is_point_in_outline(p1_close, outline) or heerim_geom.is_point_in_outline(p2_close, outline):
                                    show_junction = True

                        if show_junction and inter: junctions.append((inter, [w1.Id, w2.Id]))
                
                for i in range(len(walls)):
                    for j in range(i + 1, len(walls)):
                        w1, w2 = walls[i], walls[j]
                        c1, c2 = w1.Location.Curve, w2.Location.Curve
                        if heerim_geom.are_collinear(c1, c2):
                            pts = [c1.GetEndPoint(0), c1.GetEndPoint(1), c2.GetEndPoint(0), c2.GetEndPoint(1)]
                            d_min = 1e9; best_m = None
                            for pa in [pts[0], pts[1]]:
                                for pb in [pts[2], pts[3]]:
                                    d = pa.DistanceTo(pb)
                                    if d < d_min: d_min = d; best_m = (pa + pb) / 2.0
                            if best_m and heerim_geom.is_point_in_outline(best_m, outline):
                                junctions.append((best_m, [w1.Id, w2.Id]))

                for target_pt, involved_ids in junctions:
                    tid = create_marker_filled_square(target_pt, size=0.15)
                    if tid: target_ids.append(tid)
                    lines = []
                    for ep, wid in points_to_show:
                        if wid in involved_ids and ep.DistanceTo(target_pt) > 0.01:
                            lines.append(Line.CreateBound(ep, target_pt))
                    if lines:
                        ds_lines = DirectShape.CreateElement(doc, ElementId(BuiltInCategory.OST_GenericModel))
                        ds_lines.SetShape(lines)
                        target_ids.append(ds_lines.Id)

            # Note: box_id is already created above in the new flow
            
            ogs_red = OverrideGraphicSettings()
            ogs_red.SetSurfaceForegroundPatternColor(Color(255, 0, 0))
            patterns = FilteredElementCollector(doc).OfClass(FillPatternElement).ToElements()
            solid_pat = next((p for p in patterns if p.GetFillPattern().IsSolidFill), None)
            if solid_pat: ogs_red.SetSurfaceForegroundPatternId(solid_pat.Id)
            for mid in marker_ids:
                try: doc.ActiveView.SetElementOverrides(mid, ogs_red)
                except: pass
                
            ogs_blue = OverrideGraphicSettings()
            ogs_blue.SetProjectionLineColor(Color(0, 112, 192))
            ogs_blue.SetProjectionLineWeight(5)
            ogs_blue_filled = OverrideGraphicSettings()
            ogs_blue_filled.SetSurfaceForegroundPatternColor(Color(0, 112, 192))
            if solid_pat: ogs_blue_filled.SetSurfaceForegroundPatternId(solid_pat.Id)
            for tid in target_ids:
                try: 
                    doc.ActiveView.SetElementOverrides(tid, ogs_blue)
                    doc.ActiveView.SetElementOverrides(tid, ogs_blue_filled)
                except: pass

            # Box overrides applied early
            ogs_transparent = OverrideGraphicSettings()
            ogs_transparent.SetSurfaceTransparency(70)
            for w in walls:
                try: doc.ActiveView.SetElementOverrides(w.Id, ogs_transparent)
                except: pass

            # Center overrides applied early
            
            t_preview.Commit() # Show preview box and elements normally
            uidoc.RefreshActiveView()
            
            res = TaskDialog.Show("Auto Merge", "Previewing Points.\nProceed to Merge?", TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No)
            tg.RollBack()
            
            if res == TaskDialogResult.Yes:
                log("Proceeding...", "[DRAG]")
                t_process = Transaction(doc, "Heerim Auto Merge Process")
                t_process.Start()
                autotrim_engine.process_trim(doc, uidoc, walls, box_center, outline, None, lambda msg: log(msg, "[DRAG]"))
                t_process.Commit()
                uidoc.RefreshActiveView()
                log("Session #{} completed. Ready for next selection...".format(session_count), "[DRAG]")
            else:
                log("Cancelled by user. Skipping to next selection...", "[DRAG]")
                continue
        
        except Exception as e:
            break

# --- UI Setup ---
class AutoMergeWindow(forms.WPFWindow):
    def __init__(self, xaml_file_name):
        forms.WPFWindow.__init__(self, xaml_file_name)
        self.is_run = False
        self._setup_data()

    def Window_MouseDown(self, sender, e):
        import System
        if e.ChangedButton == System.Windows.Input.MouseButton.Left:
            self.DragMove()

    def Window_KeyDown(self, sender, e):
        import System
        if e.Key == System.Windows.Input.Key.Escape:
            self.Close()
        elif e.Key == System.Windows.Input.Key.Enter:
            self.RunCommand(sender, e)

    def _setup_data(self):
        config = load_config()
        self.RadiusTB.Text = str(config.get("search_radius_mm", 3000))
        
    def RadiusTB_KeyDown(self, sender, e):
        import System
        if e.Key == System.Windows.Input.Key.Enter:
            self.RunCommand(sender, e)

    def CloseWindow(self, sender, e):
        self.Close()

    def RunCommand(self, sender, e):
        # Validate Radius
        try:
            r = float(self.RadiusTB.Text)
            if r <= 0:
                forms.alert("검색 반경은 0보다 커야 합니다.")
                return
        except ValueError:
            forms.alert("올바른 숫자를 입력해주세요.")
            return
            
        self.is_run = True
        self.Close()

if __name__ == "__main__":
    xaml_file = os.path.join(os.path.dirname(__file__), "ui.xaml")
    window = AutoMergeWindow(xaml_file)
    window.ShowDialog()
    
    if window.is_run:
        is_click = window.ModeClick.IsChecked
        try:
            radius_mm = float(window.RadiusTB.Text)
        except:
            radius_mm = 3000.0
            
        radius_ft = radius_mm * 0.00328084
        
        # Save to config
        config = load_config()
        config["search_radius_mm"] = radius_mm
        config["search_radius_ft"] = radius_ft
        save_config(config)
        
        # Launch appropriate mode
        if is_click:
            run_click_mode(radius_ft, radius_mm)
        else:
            run_drag_mode()
