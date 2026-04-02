using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace Heerim_FamilyBrowser
{
    public class MainApplication : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            // 1. Create Ribbon Tab (if not exists)
            string tabName = "Heerim";
            try { application.CreateRibbonTab(tabName); } catch { }

            // 2. Create Ribbon Panel
            RibbonPanel panel = application.CreateRibbonPanel(tabName, "Standard Library");

            // 3. Create Push Button
            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            PushButtonData buttonData = new PushButtonData(
                "btnFamilyBrowser",
                "BIM Library\nBrowser",
                assemblyPath,
                "Heerim_FamilyBrowser.Command"
            );

            PushButton button = panel.AddItem(buttonData) as PushButton;
            button.ToolTip = "Heerim Standard BIM Library Browser (Modernized)";

            // 4. Set Icon
            try
            {
                string assemblyDir = System.IO.Path.GetDirectoryName(assemblyPath);
                string iconPath = System.IO.Path.Combine(assemblyDir, "Resources", "FamilyBrowser.png");
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
}

