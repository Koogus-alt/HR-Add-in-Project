using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using ClosedXML.Excel;

namespace Heerim_SheetsAndView
{
    public static class ExcelManager
    {
        public static void ExportToCsv(ObservableCollection<SheetItem> items)
        {
            SaveFileDialog sfd = new SaveFileDialog
            {
                Filter = "Excel Files (*.xlsx)|*.xlsx",
                FileName = "Heerim_SheetList.xlsx"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    using (var workbook = new XLWorkbook())
                    {
                        var ws = workbook.Worksheets.Add("Sheets");
                        ws.Cell(1, 1).Value = "Select";
                        ws.Cell(1, 2).Value = "Sheet Number";
                        ws.Cell(1, 3).Value = "Sheet Name";
                        ws.Range(1, 1, 1, 3).Style.Font.Bold = true;

                        int row = 2;
                        foreach (var item in items)
                        {
                            ws.Cell(row, 1).Value = item.IsSelected ? "Yes" : "No";
                            ws.Cell(row, 2).Value = item.SheetNumber;
                            ws.Cell(row, 3).Value = item.SheetName;
                            row++;
                        }
                        ws.Columns().AdjustToContents();
                        workbook.SaveAs(sfd.FileName);
                    }
                    MessageBox.Show("엑셀 추출이 완료되었습니다. (Excel Export Complete)");
                }
                catch (Exception ex)
                {
                    MessageBox.Show("저장 중 오류가 발생했습니다: " + ex.Message);
                }
            }
        }

        public static List<SheetItem> ImportFromCsv()
        {
            OpenFileDialog ofd = new OpenFileDialog
            {
                Filter = "Excel Files (*.xlsx)|*.xlsx"
            };

            List<SheetItem> importedItems = new List<SheetItem>();
            if (ofd.ShowDialog() == true)
            {
                try
                {
                    using (var workbook = new XLWorkbook(ofd.FileName))
                    {
                        var ws = workbook.Worksheet(1);
                        var rows = ws.RangeUsed().RowsUsed();
                        bool isFirst = true;
                        foreach (var row in rows)
                        {
                            if (isFirst) { isFirst = false; continue; }
                            
                            string selStr = row.Cell(1).GetString().Trim();
                            bool isSelected = selStr.Equals("Yes", StringComparison.OrdinalIgnoreCase);

                            importedItems.Add(new SheetItem
                            {
                                IsSelected = isSelected,
                                SheetNumber = row.Cell(2).GetString().Trim(),
                                SheetName = row.Cell(3).GetString().Trim()
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("엑셀 불러오기 중 오류가 발생했습니다: " + ex.Message);
                }
            }
            return importedItems;
        }
    }
}
