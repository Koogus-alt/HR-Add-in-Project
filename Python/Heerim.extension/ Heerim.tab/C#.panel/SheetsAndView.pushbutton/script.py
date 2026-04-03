# -*- coding: utf-8 -*-
import clr
from pyrevit import revit, DB, UI

# Ensure SheetsAndView assembly is referenced
try:
    clr.AddReference("Heerim_SheetsAndView")
    from Heerim_SheetsAndView import SheetsAndViewCommand
    
    # Execute the command
    cmd = SheetsAndViewCommand()
    cmd.Execute(revit.uiapp, "", DB.ElementSet())
except Exception as ex:
    UI.TaskDialog.Show("Error", "Sheets and View를 실행할 수 없습니다.\n" + str(ex))
