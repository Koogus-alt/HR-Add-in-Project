using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.UI;

namespace Heerim_FamilyBrowser
{
    public partial class MainBrowserView : UserControl
    {
        private string _basePath;
        private LibraryFolder _root;
        private FamilyPlacementHandler _handler;
        private ExternalEvent _externalEvent;
        private List<FamilyItem> _allCurrentItems = new List<FamilyItem>();

        public MainBrowserView(FamilyPlacementHandler handler, ExternalEvent externalEvent)
        {
            InitializeComponent();
            _handler = handler;
            _externalEvent = externalEvent;

            // Calculate Base Path relative to DLL location
            string assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string assemblyDir = System.IO.Path.GetDirectoryName(assemblyLocation);
            // DLL is in C#\Heerim_FamilyBrowser.extension\bin\Release\net8.0-windows\
            // Library is in Library\ (at the root of the extension)
            _basePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(assemblyDir, "Library"));
            
            LoadLibrary();
        }

        private void LoadLibrary()
        {
            try
            {
                string treePath = Path.Combine(_basePath, "tree.xml");
                if (File.Exists(treePath))
                {
                    _root = LibraryTreeParser.Parse(treePath);
                    CategoryTree.ItemsSource = _root.SubFolders;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"라이브러리 로드 오류: {ex.Message}");
            }
        }

        private void CategoryTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is LibraryFolder folder)
            {
                CategoryTitle.Text = folder.Name;
                LoadFamilies(folder.Name);
            }
        }

        private void LoadFamilies(string categoryName)
        {
            try
            {
                string categoryPath = Path.Combine(_basePath, categoryName);
                if (Directory.Exists(categoryPath))
                {
                    _allCurrentItems = FamilyLoader.LoadFromDirectory(categoryPath, categoryName);
                    FamilyListBox.ItemsSource = _allCurrentItems;
                }
                else
                {
                    _allCurrentItems.Clear();
                    FamilyListBox.ItemsSource = null;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"패밀리 로드 오류: {ex.Message}");
            }
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyFilter();
        }

        private void SearchBox_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                ApplyFilter();
            }
        }

        private void ApplyFilter()
        {
            string filter = SearchBox.Text.Trim().ToLower();
            if (string.IsNullOrEmpty(filter))
            {
                FamilyListBox.ItemsSource = _allCurrentItems;
            }
            else
            {
                var filtered = _allCurrentItems.Where(x => x.Name.ToLower().Contains(filter)).ToList();
                FamilyListBox.ItemsSource = filtered;
            }
        }

        private void FamilyListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FamilyListBox.SelectedItem is FamilyItem item)
            {
                _handler.SelectedItem = item;
                _externalEvent.Raise();
            }
        }
    }
}
