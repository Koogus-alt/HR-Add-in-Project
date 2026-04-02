using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB; // Ensure DB reference is available
using System.Reflection;

namespace RevitFamilyBrowser
{
    public class MyApplication : IExternalApplication
    {
        // Unique GUID for the dockable pane
        public static MyApplication Instance { get; private set; }
        public UIApplication UIApp { get; internal set; }
        public static readonly DockablePaneId PaneId = new DockablePaneId(new Guid("B8E591F4-13D9-4B82-AE5F-2947FBC3C174"));
        
        public static ParameterInjectUpdater ParameterUpdater { get; private set; }

        public Result OnStartup(UIControlledApplication application)
        {
            Instance = this;
            // Register Dockable Pane
            RegisterDockablePane(application);

            // Register UI events for document switching
            application.ViewActivated += OnViewActivated;

            // NEW: Create Ribbon Tab and Button
            CreateRibbon(application);

            try
            {
                // v97 Register Parameter Inject Updater
                ParameterUpdater = new ParameterInjectUpdater(application.ActiveAddInId);
                UpdaterRegistry.RegisterUpdater(ParameterUpdater, true); // v161: Make updater optional to prevent warnings on other PCs
                
                // Trigger on Element Added
                ElementClassFilter familyInstanceFilter = new ElementClassFilter(typeof(FamilyInstance));
                UpdaterRegistry.AddTrigger(ParameterUpdater.GetUpdaterId(), familyInstanceFilter, Element.GetChangeTypeElementAddition());
                
                // Also trigger on System Family instances like Walls, Floors, etc.
                ElementCategoryFilter wallFilter = new ElementCategoryFilter(BuiltInCategory.OST_Walls);
                UpdaterRegistry.AddTrigger(ParameterUpdater.GetUpdaterId(), wallFilter, Element.GetChangeTypeElementAddition());
                
                ElementCategoryFilter floorFilter = new ElementCategoryFilter(BuiltInCategory.OST_Floors);
                UpdaterRegistry.AddTrigger(ParameterUpdater.GetUpdaterId(), floorFilter, Element.GetChangeTypeElementAddition());
            }
            catch (Exception ex)
            {
                TaskDialog.Show("IUpdater ?깅줉 ?ㅻ쪟", ex.Message + "\n" + ex.StackTrace);
            }

            return Result.Succeeded;
        }

        private void OnViewActivated(object sender, Autodesk.Revit.UI.Events.ViewActivatedEventArgs e)
        {
            if (UIApp == null) UIApp = sender as UIApplication;
            if (e.Document == null) return;
            if (e.PreviousActiveView != null && e.PreviousActiveView.Document.Equals(e.CurrentActiveView.Document))
            {
                // Switched view within the same document, no need to refresh families usually
                return;
            }

            // The document has changed. Refresh the Smart Assist pane if it is visible
            if (FamilyBrowserView.Instance != null)
            {
                FamilyBrowserView.Instance.RefreshData();
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            application.ViewActivated -= OnViewActivated;
            
            if (ParameterUpdater != null)
            {
                UpdaterRegistry.UnregisterUpdater(ParameterUpdater.GetUpdaterId());
            }

            return Result.Succeeded;
        }

        private void RegisterDockablePane(UIControlledApplication application)
        {
            FamilyBrowserView page = new FamilyBrowserView();
            DockablePaneProviderData data = new DockablePaneProviderData();
            // Assign the visual element to the provider data
            data.FrameworkElement = page as System.Windows.FrameworkElement;
            // Provide initial state (docked left)
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Right,
            };

            // Register with Revit
            try 
            {
                application.RegisterDockablePane(PaneId, "Smart Assist", page as IDockablePaneProvider);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", "Failed to register dockable pane: " + ex.Message);
            }
        }

        private void CreateRibbon(UIControlledApplication application)
        {
            // 1. Create Ribbon Tab (if not exists)
            string tabName = "Heerim";
            try { application.CreateRibbonTab(tabName); } catch { }

            // 2. Create Ribbon Panel
            RibbonPanel panel = null;
            List<RibbonPanel> panels = application.GetRibbonPanels(tabName);
            foreach (RibbonPanel p in panels)
            {
                if (p.Name == "Work") { panel = p; break; }
            }
            if (panel == null) panel = application.CreateRibbonPanel(tabName, "Work");

            // 3. Create Push Button
            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            PushButtonData buttonData = new PushButtonData(
                "btnSmartAssist",
                "Smart\nAssist",
                assemblyPath,
                "RevitFamilyBrowser.TogglePaneCommand"
            );

            PushButton button = panel.AddItem(buttonData) as PushButton;
            button.ToolTip = "Toggle Heerim Smart Assist Panel";

            // 4. Set Icon (Large Image)
            try
            {
                string iconPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(assemblyPath), "Resources", "SmartAssist.png");
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
        }
    }

    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class TogglePaneCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            DockablePane pane = commandData.Application.GetDockablePane(MyApplication.PaneId);
            if (pane.IsShown()) pane.Hide();
            else pane.Show();
            return Result.Succeeded;
        }
    }
}

