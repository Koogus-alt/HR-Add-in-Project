using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Heerim_AutoPlacement
{
    public class AutoPlacementCommand
    {
        public static void Run(UIApplication uiApp)
        {
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;

            // WPF 창 인스턴스 생성
            MainView view = new MainView();

            // Revit 데이터를 ViewModel에 로드하는 로직 (추후 구현)
            LoadRevitData(doc, view.ViewModel);

            // 창 표시
            if (view.ShowDialog() == true)
            {
                // 사용자가 Apply를 눌렀을 때 실행될 메인 로직
                using (Transaction trans = new Transaction(doc, "Heerim Auto Placement"))
                {
                    trans.Start();
                    try
                    {
                        ExecutePlacement(doc, view.ViewModel, out int viewCount, out int sheetCount, out ElementId firstViewId, out ElementId firstSheetId);
                        trans.Commit();

                        // 결과화면 자동 열기 (시트 우선, 없으면 뷰)
                        if (firstSheetId != ElementId.InvalidElementId)
                        {
                            uiDoc.ActiveView = doc.GetElement(firstSheetId) as View;
                        }
                        else if (firstViewId != ElementId.InvalidElementId)
                        {
                            uiDoc.ActiveView = doc.GetElement(firstViewId) as View;
                        }

                        string message = $"도면 자동 배치가 완료되었습니다.\n\n" +
                                         $"- 생성된 뷰: {viewCount}개\n" +
                                         $"- 생성된 시트: {sheetCount}개";
                        
                        if (viewCount == 0 && sheetCount == 0)
                        {
                            TaskDialog.Show("Warning", message + "\n\n(체크박스로 항목을 선택했는지 확인해주세요.)");
                        }
                        else
                        {
                            TaskDialog.Show("Success", message);
                        }
                    }
                    catch (Exception ex)
                    {
                        trans.RollBack();
                        TaskDialog.Show("Error", "실행 중 오류가 발생했습니다: " + ex.Message);
                    }
                }
            }
        }

        private static void LoadRevitData(Document doc, MainViewModel viewModel)
        {
            // 실제 Revit Document에서 도면층, 시트 정보를 수집하여 ViewModel에 채움
            viewModel.LoadData(doc);
        }

        private static void ExecutePlacement(Document doc, MainViewModel viewModel, out int viewCount, out int sheetCount, out ElementId firstViewId, out ElementId firstSheetId)
        {
            viewCount = 0;
            sheetCount = 0;
            firstViewId = ElementId.InvalidElementId;
            firstSheetId = ElementId.InvalidElementId;

            // 1. 뷰 생성 (개별 행 설정을 사용하도록 변경됨)
            var createdViews = SheetAutomation.CreateViews(doc, viewModel.ViewItems);
            viewCount = createdViews.Count;
            if (createdViews.Any()) firstViewId = createdViews.First().Id;

            // 2. 시트 생성
            var createdSheets = SheetAutomation.CreateSheets(doc, viewModel.SheetItems, viewModel.SelectedTitleBlock);
            sheetCount = createdSheets.Count;
            if (createdSheets.Any()) firstSheetId = createdSheets.First().Id;

            // 3. 뷰포트 배치 (예시: 첫 번째 시트에 첫 번째 뷰 배치 - 실제로는 사용자 선택에 따름)
            // 기획안에 따르면 대량 배치가 목적이므로 자동 매칭 또는 선택된 쌍을 기반으로 배치
            if (createdSheets.Any() && createdViews.Any())
            {
                // 선택된 레이아웃 포인트 (여러 개일 수 있음)
                var selectedPoints = viewModel.LayoutPoints.Where(p => p.IsSelected).ToList();
                int pointIdx = 0;

                foreach (var sheet in createdSheets)
                {
                    foreach (var view in createdViews)
                    {
                        if (pointIdx < selectedPoints.Count)
                        {
                            LayoutEngine.PlaceViewOnSheet(sheet, view, selectedPoints[pointIdx].Number);
                            pointIdx++;
                        }
                    }
                }
            }
        }
    }
}
