# -*- coding: utf-8 -*-
import clr
from pyrevit import revit, DB, UI

# Ensure SmartAssist assembly is referenced
try:
    clr.AddReference("SmartAssist")
    from Heerim_SmartAssist import TogglePaneCommand
    
    # Execute the command
    cmd = TogglePaneCommand()
    cmd.Execute(revit.uiapp, "", DB.ElementSet())
except Exception as ex:
    UI.TaskDialog.Show("Error", "Smart Assist를 실행할 수 없습니다.\n" + str(ex))
