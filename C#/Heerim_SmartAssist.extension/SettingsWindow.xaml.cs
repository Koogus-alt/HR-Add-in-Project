using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;

namespace Heerim_SmartAssist
{
    public class CategorySelectionItem
    {
        public string Name { get; set; }
        public bool IsSelected { get; set; }
    }

    public partial class SettingsWindow : Window
    {
        private int _initialSortMode;
        private Document _doc;

        private List<CategorySelectionItem> _familyCategoriesSource = new List<CategorySelectionItem>();
        private List<CategorySelectionItem> _annotateCategoriesSource = new List<CategorySelectionItem>();

        public SettingsWindow(int currentSortMode, Document doc)
        {
            InitializeComponent();
            _initialSortMode = currentSortMode;
            _doc = doc;

            // Load sorting
            if (currentSortMode == 0) SortModeNone.IsChecked = true;
            else if (currentSortMode == 1) SortModeCategory.IsChecked = true;
            else if (currentSortMode == 2) SortModeHeader.IsChecked = true;

            LoadCategories();
        }

        private void LoadCategories()
        {
            if (_doc == null) return;

            // 1. All Unique Family/Model Categories present in document (that have actual symbols)
            var modelCategories = new FilteredElementCollector(_doc)
                .WhereElementIsElementType()
                .Cast<ElementType>()
                .Where(x => x.Category != null && x.Category.CategoryType == CategoryType.Model)
                .Select(x => x.Category.Name)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            // 2. All Unique Annotation Categories 
            var annotateCategories = new FilteredElementCollector(_doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(x => x.Category != null && (x.Category.CategoryType == CategoryType.Annotation || x.Category.Id.Value == (long)BuiltInCategory.OST_DetailComponents))
                .Select(x => x.Category.Name)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            // Load saved preferences
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appDataDir = System.IO.Path.Combine(appDataPath, "Autodesk", "Revit", "Addins", "2026", "RevitFamilyBrowser");
            
            var savedFamily = LoadSavedList(System.IO.Path.Combine(appDataDir, "categories_family.txt"));
            var savedAnnotate = LoadSavedList(System.IO.Path.Combine(appDataDir, "categories_annotate.txt"));

            // Populate Family Checkboxes
            foreach (var cat in modelCategories)
            {
                bool isChecked = savedFamily.Count == 0 ? IsDefaultFamilyCategory(cat) : savedFamily.Contains(cat);
                _familyCategoriesSource.Add(new CategorySelectionItem { Name = cat, IsSelected = isChecked });
            }

            // Populate Annotate Checkboxes
            foreach (var cat in annotateCategories)
            {
                bool isChecked = savedAnnotate.Count == 0 ? true : savedAnnotate.Contains(cat); // Annotate defaults to ALL
                _annotateCategoriesSource.Add(new CategorySelectionItem { Name = cat, IsSelected = isChecked });
            }

            // Initial binding
            CategoryListControl.ItemsSource = _familyCategoriesSource;
        }

        private bool IsDefaultFamilyCategory(string catName)
        {
            // Approximate matching for default categories mentioned in DataLoader
            return catName.Contains("가구") || catName.Contains("일반 모델") || catName.Contains("특수 설비") || 
                   catName.Contains("배관 설비") || catName.Contains("조명 기구") || catName.Contains("기계 장비") || 
                   catName.Contains("창") || catName.Contains("문");
        }

        private HashSet<string> LoadSavedList(string path)
        {
            var set = new HashSet<string>();
            if (System.IO.File.Exists(path))
            {
                var lines = System.IO.File.ReadAllLines(path);
                foreach (var line in lines)
                {
                    if (!string.IsNullOrWhiteSpace(line)) set.Add(line.Trim());
                }
            }
            return set;
        }

        private void TabTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CategoryListControl == null) return;

            if (TabTypeCombo.SelectedIndex == 0)
                CategoryListControl.ItemsSource = _familyCategoriesSource;
            else
                CategoryListControl.ItemsSource = _annotateCategoriesSource;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                OnCancelClick(null, null);
            }
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            // 1. Save Sort Mode
            int mode = 0;
            if (SortModeCategory.IsChecked == true) mode = 1;
            else if (SortModeHeader.IsChecked == true) mode = 2;
            SmartAssistItem.FavoritesSortMode = mode;

            // 2. Save Categories
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appDataDir = System.IO.Path.Combine(appDataPath, "Autodesk", "Revit", "Addins", "2026", "RevitFamilyBrowser");
            System.IO.Directory.CreateDirectory(appDataDir);

            System.IO.File.WriteAllLines(
                System.IO.Path.Combine(appDataDir, "categories_family.txt"), 
                _familyCategoriesSource.Where(x => x.IsSelected).Select(x => x.Name)
            );

            System.IO.File.WriteAllLines(
                System.IO.Path.Combine(appDataDir, "categories_annotate.txt"), 
                _annotateCategoriesSource.Where(x => x.IsSelected).Select(x => x.Name)
            );

            this.DialogResult = true;
            this.Close();
        }
    }
}
