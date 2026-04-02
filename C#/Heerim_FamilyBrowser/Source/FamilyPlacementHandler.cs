using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Heerim_FamilyBrowser
{
    public class FamilyPlacementHandler : IExternalEventHandler
    {
        public FamilyItem SelectedItem { get; set; }

        public void Execute(UIApplication app)
        {
            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;
            Document doc = uidoc.Document;

            if (SelectedItem == null) return;

            using (Transaction trans = new Transaction(doc, "Load and Place Family"))
            {
                trans.Start();
                
                Family family;
                // Try to load the family
                if (doc.LoadFamily(SelectedItem.FullPath, out family))
                {
                    // Get the first symbol
                    ElementId symbolId = family.GetFamilySymbolIds().GetEnumerator().MoveNext() 
                        ? family.GetFamilySymbolIds().GetEnumerator().Current 
                        : ElementId.InvalidElementId;

                    if (symbolId != ElementId.InvalidElementId)
                    {
                        FamilySymbol symbol = doc.GetElement(symbolId) as FamilySymbol;
                        if (symbol != null && !symbol.IsActive) symbol.Activate();

                        // Trigger placement
                        uidoc.PromptForFamilyInstancePlacement(symbol);
                    }
                }
                else
                {
                    // Family might already be loaded, try to find it
                    var loadedFamily = new FilteredElementCollector(doc)
                        .OfClass(typeof(Family))
                        .Cast<Family>()
                        .FirstOrDefault(f => f.Name == SelectedItem.Name);

                    if (loadedFamily != null)
                    {
                        FamilySymbol symbol = doc.GetElement(loadedFamily.GetFamilySymbolIds().First()) as FamilySymbol;
                        if (symbol != null && !symbol.IsActive) symbol.Activate();
                        uidoc.PromptForFamilyInstancePlacement(symbol);
                    }
                }

                trans.Commit();
            }
        }

        public string GetName()
        {
            return "Heerim Family Placement Handler";
        }
    }
}
