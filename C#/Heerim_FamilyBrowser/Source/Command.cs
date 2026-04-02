using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Windows;

namespace Heerim_FamilyBrowser
{
    [Transaction(TransactionMode.Manual)]
    public class Command : IExternalCommand
    {
        private static FamilyPlacementHandler _handler = new FamilyPlacementHandler();
        private static ExternalEvent _externalEvent = ExternalEvent.Create(_handler);

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // Create the window to host our UserControl
                var window = new Window
                {
                    Title = "Heerim BIM Library Browser (Modern)",
                    Content = new MainBrowserView(_handler, _externalEvent),
                    Width = 900,
                    Height = 600,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };

                // Set Revit Window as Owner (Optional, but good for focus)
                // System.Windows.Interop.WindowInteropHelper helper = new System.Windows.Interop.WindowInteropHelper(window);
                // helper.Owner = commandData.Application.MainWindowHandle;

                window.Show();

                return Result.Succeeded;
            }
            catch (System.Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
