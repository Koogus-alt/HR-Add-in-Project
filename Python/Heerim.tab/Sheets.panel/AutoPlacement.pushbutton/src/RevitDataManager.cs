using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace Heerim_AutoPlacement
{
    public static class RevitDataManager
    {
        public static List<Level> GetLevels(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();
        }

        public static List<ViewPlan> GetPlanViews(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(v => !v.IsTemplate)
                .ToList();
        }

        public static List<ViewSheet> GetSheets(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .ToList();
        }

        public static List<Element> GetScopeBoxes(Document doc)
        {
            try
            {
                return new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                    .WhereElementIsNotElementType()
                    .ToList();
            }
            catch { return new List<Element>(); }
        }

        public static List<FamilySymbol> GetTitleBlocks(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .ToList();
        }

        public static List<ViewPlan> GetViewTemplates(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(v => v.IsTemplate)
                .ToList();
        }
    }
}
