using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using System.Reflection;

namespace Heerim_SheetsAndView
{
    public class MainApplication : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            // NEW: Create Ribbon Tab and Button (MOVED TO pyRevit)
            /*
            string tabName = "Heerim";
            try { application.CreateRibbonTab(tabName); } catch { }

            RibbonPanel panel = null;
            List<RibbonPanel> panels = application.GetRibbonPanels(tabName);
            foreach (RibbonPanel p in panels)
            {
                if (p.Name == "Sheets") { panel = p; break; }
            }
            if (panel == null) panel = application.CreateRibbonPanel(tabName, "Sheets");

            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            PushButtonData buttonData = new PushButtonData(
                "btnSheetsAndView",
                "Sheets\nand View",
                assemblyPath,
                "Heerim_SheetsAndView.SheetsAndViewCommand"
            );

            PushButton button = panel.AddItem(buttonData) as PushButton;
            button.ToolTip = "Create Sheets and Views automatically from Excel.";

            // Load Icon (Robust logic)
            try
            {
                string assemblyDir = System.IO.Path.GetDirectoryName(assemblyPath);
                string iconPath = System.IO.Path.Combine(assemblyDir, "Resources", "SheetsAndView.png");
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
            */

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}

