using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace Heerim_SheetsAndView
{
    public static class SheetAutomation
    {
        public static List<ViewPlan> CreateViews(Document doc, IEnumerable<ViewItem> items)
        {
            List<ViewPlan> createdViews = new List<ViewPlan>();
            var templates = RevitDataManager.GetViewTemplates(doc);
            var scopeBoxes = RevitDataManager.GetScopeBoxes(doc);

            ViewPlan lastParentView = null;

            foreach (var item in items.Where(i => i.IsSelected))
            {
                Level level = RevitDataManager.GetLevels(doc).FirstOrDefault(l => l.Name == item.LevelName);
                if (level == null) continue;

                // Category에 따른 뷰 유형 선택
                ElementId viewTypeId = (item.Category == "Ceiling Plan") 
                    ? doc.GetDefaultElementTypeId(ElementTypeGroup.ViewTypeCeilingPlan) 
                    : doc.GetDefaultElementTypeId(ElementTypeGroup.ViewTypeFloorPlan);

                for (int i = 0; i < item.Count; i++)
                {
                    ViewPlan newView = null;

                    if (item.IsDependentView && lastParentView != null)
                    {
                        // 진짜 종속 뷰 복제 (부모가 있을 때만)
                        ElementId newViewId = lastParentView.Duplicate(ViewDuplicateOption.AsDependent);
                        newView = doc.GetElement(newViewId) as ViewPlan;
                    }
                    else
                    {
                        // 독립 뷰 생성 (부모 뷰가 없거나 IsDependentView가 false인 경우)
                        // 기존 방식: 모델 내 기존 뷰가 있으면 복제, 없으면 새로 생성
                        ViewPlan modelParent = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewPlan))
                            .Cast<ViewPlan>()
                            .FirstOrDefault(v => !v.IsTemplate && v.GenLevel != null && v.GenLevel.Id == level.Id && (item.Category == "Floor Plan" ? v.ViewType == ViewType.FloorPlan : v.ViewType == ViewType.CeilingPlan));

                        if (modelParent != null)
                        {
                            ElementId newViewId = modelParent.Duplicate(ViewDuplicateOption.Duplicate);
                            newView = doc.GetElement(newViewId) as ViewPlan;
                        }
                        else
                        {
                            newView = ViewPlan.Create(doc, viewTypeId, level.Id);
                        }

                        // 이 뷰를 다음 자식들의 부모로 기억
                        lastParentView = newView;
                    }
                    
                    if (newView == null) continue;

                    try { newView.DetailLevel = ViewDetailLevel.Fine; } catch { }
                    try { newView.Discipline = ViewDiscipline.Architectural; } catch { }

                    string suffix = (item.Count > 1) ? $"_{i + 1}" : "";
                    if (!string.IsNullOrWhiteSpace(item.ViewName))
                        newView.Name = item.ViewName + suffix;

                    if (item.SelectedViewTemplate != "(None)")
                    {
                        var template = templates.FirstOrDefault(t => t.Name == item.SelectedViewTemplate);
                        if (template != null) newView.ViewTemplateId = template.Id;
                    }

                    if (item.SelectedScopeBox != "(None)")
                    {
                        var scopeBox = scopeBoxes.FirstOrDefault(s => s.Name == item.SelectedScopeBox);
                        if (scopeBox != null)
                        {
                            Parameter p = newView.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                            if (p != null) p.Set(scopeBox.Id);
                        }
                    }

                    // UserCropBox 로직 적용
                    if (item.HasCropBox && item.UserCropBox != null)
                    {
                        try
                        {
                            doc.Regenerate();
                            newView.CropBoxActive = true;
                            newView.CropBoxVisible = true;

                            BoundingBoxXYZ viewCrop = newView.CropBox;
                            Transform viewTransform = viewCrop.Transform;
                            Transform inverseTransform = viewTransform.Inverse;

                            XYZ localMin = inverseTransform.OfPoint(item.UserCropBox.Min);
                            XYZ localMax = inverseTransform.OfPoint(item.UserCropBox.Max);

                            double minX = Math.Min(localMin.X, localMax.X);
                            double minY = Math.Min(localMin.Y, localMax.Y);
                            double maxX = Math.Max(localMin.X, localMax.X);
                            double maxY = Math.Max(localMin.Y, localMax.Y);

                            viewCrop.Min = new XYZ(minX, minY, viewCrop.Min.Z);
                            viewCrop.Max = new XYZ(maxX, maxY, viewCrop.Max.Z);

                            newView.CropBox = viewCrop;
                        }
                        catch { }
                    }

                    createdViews.Add(newView);
                }
            }
            return createdViews;
        }

        public static List<ViewSheet> CreateSheets(Document doc, IEnumerable<SheetItem> items, string titleBlockName)
        {
            FamilySymbol titleBlock = RevitDataManager.GetTitleBlocks(doc).FirstOrDefault(t => t.Name == titleBlockName);
            if (titleBlock == null) return new List<ViewSheet>();

            if (!titleBlock.IsActive) titleBlock.Activate();

            List<ViewSheet> createdSheets = new List<ViewSheet>();
            foreach (var item in items.Where(i => i.IsSelected))
            {
                ViewSheet sheet = ViewSheet.Create(doc, titleBlock.Id);
                if (!string.IsNullOrWhiteSpace(item.SheetNumber))
                    sheet.SheetNumber = item.SheetNumber;
                if (!string.IsNullOrWhiteSpace(item.SheetName))
                    sheet.Name = item.SheetName;
                createdSheets.Add(sheet);
            }
            return createdSheets;
        }
    }
}
