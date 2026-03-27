# -*- coding: utf-8 -*-
import clr
import System
from pyrevit import forms

# Dockable Pane ID from the C# MyApplication.cs
pane_guid = System.Guid("B8E591F4-13D9-4B82-AE5F-2947FBC3C174")

clr.AddReference("RevitAPIUI")
from Autodesk.Revit.UI import DockablePaneId

pane_id = DockablePaneId(pane_guid)

try:
    pane = __revit__.GetDockablePane(pane_id)
    if pane.IsShown():
        pane.Hide()
    else:
        pane.Show()
except Exception as e:
    forms.alert("Smart Assist 패널을 열 수 없습니다.\n레빗 시작 시 애드인이 정상 로드되지 않았을 수 있습니다.\n\n(레빗 재시작이 필요합니다)\n\n에러 상세: " + str(e))
