using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Heerim_SmartAssist
{
    [Transaction(TransactionMode.Manual)]
    public class MyCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                DockablePaneId paneId = MyApplication.PaneId;
                DockablePane pane = commandData.Application.GetDockablePane(paneId);
                
                if (pane != null)
                {
                    if (pane.IsShown())
                    {
                        pane.Hide();
                    }
                    else
                    {
                        pane.Show();
                    }
                }
                else
                {
                    TaskDialog.Show("Error", "Could not find the Dockable Pane.");
                    return Result.Failed;
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
