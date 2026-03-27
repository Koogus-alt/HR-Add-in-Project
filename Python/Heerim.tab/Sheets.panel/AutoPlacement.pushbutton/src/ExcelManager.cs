using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace Heerim_AutoPlacement
{
    public static class ExcelManager
    {
        public static void ExportToCsv(ObservableCollection<SheetItem> items)
        {
            SaveFileDialog sfd = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv",
                FileName = "Heerim_SheetList.csv"
            };

            if (sfd.ShowDialog() == true)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Select,Sheet Number,Sheet Name");
                foreach (var item in items)
                {
                    sb.AppendLine($"{(item.IsSelected ? "Yes" : "No")},{item.SheetNumber},{item.SheetName}");
                }
                File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
                MessageBox.Show("수출이 완료되었습니다.");
            }
        }

        public static List<SheetItem> ImportFromCsv()
        {
            OpenFileDialog ofd = new OpenFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv"
            };

            List<SheetItem> importedItems = new List<SheetItem>();

            if (ofd.ShowDialog() == true)
            {
                try
                {
                    var lines = File.ReadAllLines(ofd.FileName);
                    // Skip header
                    foreach (var line in lines.Skip(1))
                    {
                        var parts = line.Split(',');
                        if (parts.Length >= 3)
                        {
                            importedItems.Add(new SheetItem
                            {
                                IsSelected = parts[0].Trim().Equals("Yes", StringComparison.OrdinalIgnoreCase),
                                SheetNumber = parts[1].Trim(),
                                SheetName = parts[2].Trim()
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("불러오기 중 오류가 발생했습니다: " + ex.Message);
                }
            }
            return importedItems;
        }
    }
}
