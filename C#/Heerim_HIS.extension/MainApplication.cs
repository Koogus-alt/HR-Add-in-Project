using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace Heerim_HIS
{
    public class MainApplication : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            // 1. Create/Get Ribbon Tab
            string tabName = "Heerim";
            try { application.CreateRibbonTab(tabName); } catch { }

            // 2. Create Ribbon Panel for HIS
            RibbonPanel panel = null;
            List<RibbonPanel> panels = application.GetRibbonPanels(tabName);
            foreach (RibbonPanel p in panels)
            {
                if (p.Name == "HIS Data") { panel = p; break; }
            }
            if (panel == null) panel = application.CreateRibbonPanel(tabName, "HIS Data");

            // 3. Create Smart Filter Button
            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            PushButtonData buttonData = new PushButtonData(
                "btnSmartFilter",
                "Smart\nFilter",
                assemblyPath,
                "Heerim_HIS.Command"
            );

            PushButton button = panel.AddItem(buttonData) as PushButton;
            button.ToolTip = "Heerim HIS Smart Filter: Search and Select Elements";

            // Set Icon
            try
            {
                string assemblyDir = System.IO.Path.GetDirectoryName(assemblyPath);
                string iconPath = System.IO.Path.Combine(assemblyDir, "Resources", "Heerim_HIS.png");
                if (System.IO.File.Exists(iconPath))
                {
                    BitmapImage image = new BitmapImage();
                    image.BeginInit();
                    image.UriSource = new Uri(iconPath, UriKind.Absolute);
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.EndInit();
                    button.LargeImage = image;
                }
            }
            catch { }

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }

    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class Command : IExternalCommand
    {
        public Autodesk.Revit.UI.Result Execute(
            Autodesk.Revit.UI.ExternalCommandData commandData, 
            ref string message, 
            Autodesk.Revit.DB.ElementSet elements)
        {
            // Launch the Smart Filter Window
            SmartFilterView window = new SmartFilterView(commandData.Application);
            window.Show();
            return Autodesk.Revit.UI.Result.Succeeded;
        }
    }
}

