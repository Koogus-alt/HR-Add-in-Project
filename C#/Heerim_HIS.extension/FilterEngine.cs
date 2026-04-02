using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Heerim_HIS
{
    public class FilterEngine
    {
        private UIApplication _uiApp;
        private Document _doc;

        public FilterEngine(UIApplication uiApp)
        {
            _uiApp = uiApp;
            _doc = uiApp.ActiveUIDocument.Document;
        }

        /// <summary>
        /// Searches elements by name (Category, Family, or Type name)
        /// </summary>
        public List<Element> SearchElementsByText(string searchText)
        {
            if (string.IsNullOrEmpty(searchText)) return new List<Element>();

            searchText = searchText.ToLower();

            // Collect all model elements (Instance + Type)
            var collector = new FilteredElementCollector(_doc)
                .WhereElementIsNotElementType()
                .Cast<Element>();

            // Perform filtering
            var results = collector.Where(e => 
                e.Name.ToLower().Contains(searchText) || 
                e.Category?.Name.ToLower().Contains(searchText) == true
            ).ToList();

            return results;
        }

        public void SelectElements(List<ElementId> ids)
        {
            if (ids == null || ids.Count == 0) return;
            _uiApp.ActiveUIDocument.Selection.SetElementIds(ids);
        }
    }
}
