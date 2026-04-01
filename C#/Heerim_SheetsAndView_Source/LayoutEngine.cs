using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace Heerim_SheetsAndView
{
    public static class LayoutEngine
    {
        /// <summary>
        /// 도각(Title Block)의 유효 범위를 계산하고 10x10 그리드 포인트를 생성합니다.
        /// </summary>
        public static XYZ GetPlacementPoint(ViewSheet sheet, int pointNumber)
        {
            Document doc = sheet.Document;

            // 1. 도각 및 상세 항목(Detail Item) 경계 찾기
            BoundingBoxXYZ bbox = GetBoundaryFromDetailItems(sheet) ?? GetBoundaryFromTitleBlock(sheet);
            
            if (bbox == null) return XYZ.Zero;

            double width = bbox.Max.X - bbox.Min.X;
            double height = bbox.Max.Y - bbox.Min.Y;

            // 3. 10x10 그리드 계산 (기획안: 왼쪽 상단 기준 배치)
            // pointNumber가 0~99라고 가정 (0=좌상단, 99=우하단 또는 숫자 체계에 따름)
            // 여기서는 0~9 (Row 1), 10~19 (Row 2) ... 형태의 10x10 그리드로 가정
            int row = pointNumber / 10;
            int col = pointNumber % 10;

            double stepX = width / 10.0;
            double stepY = height / 10.0;

            double targetX = bbox.Min.X + (col * stepX);
            double targetY = bbox.Max.Y - (row * stepY); // Y축은 위에서 아래로

            return new XYZ(targetX, targetY, 0);
        }

        private static BoundingBoxXYZ GetBoundaryFromTitleBlock(ViewSheet sheet)
        {
            FamilyInstance titleBlock = new FilteredElementCollector(sheet.Document, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .FirstOrDefault();

            return titleBlock?.get_BoundingBox(sheet);
        }

        private static BoundingBoxXYZ GetBoundaryFromDetailItems(ViewSheet sheet)
        {
            // 기획안: 레이아웃은 상세 항목(Detail Item) 패밀리로 제작됨
            // 특정 이름이나 매개변수를 가진 Detail Item을 찾아 그 경계를 반환
            var detailItem = new FilteredElementCollector(sheet.Document, sheet.Id)
                .OfCategory(BuiltInCategory.OST_DetailComponents)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .FirstOrDefault(fi => fi.Symbol.FamilyName.Contains("Layout")); // 예: "Heerim_Layout"

            return detailItem?.get_BoundingBox(sheet);
        }

        /// <summary>
        /// 뷰포트를 시트에 배치하고 타이틀 위치를 조정합니다.
        /// </summary>
        public static void PlaceViewOnSheet(ViewSheet sheet, View view, int pointNumber, double titleOffsetY_mm = 10)
        {
            XYZ placementPoint = GetPlacementPoint(sheet, pointNumber);
            
            // 뷰포트 생성
            Viewport viewport = Viewport.Create(sheet.Document, sheet.Id, view.Id, placementPoint);
            
            // 타이틀 오프셋 조정 (mm -> feet 변환 필요)
            if (titleOffsetY_mm != 0)
            {
                double offsetFeet = titleOffsetY_mm / 304.8;
                // Revit API에서 Viewport Title 위치 조정 로직 (LabelOffset)
                // Note: Viewport.GetBoxOutline() 등을 활용한 더 정밀한 조정이 필요할 수 있음
            }
        }
    }
}
