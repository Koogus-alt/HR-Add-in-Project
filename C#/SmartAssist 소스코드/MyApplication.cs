using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB; // Ensure DB reference is available

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
                TaskDialog.Show("IUpdater 등록 오류", ex.Message + "\n" + ex.StackTrace);
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
    }
}
