using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace Heerim_AutoPlacement
{
    public static class SheetAutomation
    {
        public static List<ViewPlan> CreateViews(Document doc, IEnumerable<ViewItem> items)
        {
            List<ViewPlan> createdViews = new List<ViewPlan>();
            var templates = RevitDataManager.GetViewTemplates(doc);
            var scopeBoxes = RevitDataManager.GetScopeBoxes(doc);

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
                    ViewPlan newView = ViewPlan.Create(doc, viewTypeId, level.Id);
                    
                    // v15: 뷰 가독성 개선 (상세 수준, 분야)
                    try { newView.DetailLevel = ViewDetailLevel.Fine; } catch { }
                    try { newView.Discipline = ViewDiscipline.Architectural; } catch { }

                    // 이름 설정 
                    string suffix = (item.Count > 1) ? $"_{i + 1}" : "";
                    newView.Name = item.ViewName + suffix;

                    // 개별 템플릿 적용
                    if (item.SelectedViewTemplate != "(None)")
                    {
                        var template = templates.FirstOrDefault(t => t.Name == item.SelectedViewTemplate);
                        if (template != null) newView.ViewTemplateId = template.Id;
                    }

                    // 개별 스코프 박스 적용
                    if (item.SelectedScopeBox != "(None)")
                    {
                        var scopeBox = scopeBoxes.FirstOrDefault(s => s.Name == item.SelectedScopeBox);
                        if (scopeBox != null)
                        {
                            Parameter p = newView.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                            if (p != null) p.Set(scopeBox.Id);

                            // v15: 크롭 활성화 강제
                            newView.CropBoxActive = true;
                            newView.CropBoxVisible = true;
                        }
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
                sheet.SheetNumber = item.SheetNumber;
                sheet.Name = item.SheetName;
                createdSheets.Add(sheet);
            }
            return createdSheets;
        }
    }
}
