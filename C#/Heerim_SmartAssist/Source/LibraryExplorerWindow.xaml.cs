using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using MediaColor = System.Windows.Media.Color;

namespace Heerim_SmartAssist
{
    public class LibraryFamilyItem
    {
        public string Name { get; set; } = "";
        public string RfaPath { get; set; } = "";
        public string PngPath { get; set; } = "";
        public string XmlPath { get; set; } = "";
        public string CategoryFolder { get; set; } = "";
        public BitmapImage? Thumbnail { get; set; }
        public bool IsSelected { get; set; }
    }

    public partial class LibraryExplorerWindow : Window
    {
        private Document _doc;
        private string _libraryRootPath = "";
        private List<LibraryFamilyItem> _allFamilies = new List<LibraryFamilyItem>();
        private List<LibraryFamilyItem> _displayedFamilies = new List<LibraryFamilyItem>();
        private LibraryFamilyItem? _selectedFamily = null;

        public LibraryExplorerWindow(Document doc)
        {
            InitializeComponent();
            _doc = doc;

            // Try to find the library path
            _libraryRootPath = FindLibraryPath();

            if (!string.IsNullOrEmpty(_libraryRootPath) && Directory.Exists(_libraryRootPath))
            {
                LibraryPathText.Text = _libraryRootPath;
                StatusText.Text = "카테고리를 선택하여 패밀리를 탐색하세요";
                LoadCategoryTree();
            }
            else
            {
                StatusText.Text = "라이브러리를 찾을 수 없습니다. 프로젝트 폴더 안에 '서버라이브러리' 폴더를 확인하세요.";
            }
        }

        private string FindLibraryPath()
        {
            // 1. Check relative to the current assembly (DLL location) - Best for deployment
            try
            {
                string assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string assemblyDir = Path.GetDirectoryName(assemblyPath);
                if (!string.IsNullOrEmpty(assemblyDir))
                {
                    string relativePath = Path.Combine(assemblyDir, "서버라이브러리");
                    if (Directory.Exists(relativePath)) return relativePath;
                }
            }
            catch { }

            // 2. Check the specific project directory requested by the user
            string projectPath = @"C:\Users\thomashj\Desktop\HR Add-in Project\C#\Heerim_SmartAssist\서버라이브러리";
            if (Directory.Exists(projectPath)) return projectPath;

            // 3. Fallback search common locations
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string[] candidates = new[]
            {
                Path.Combine(desktop, "서버라이브러리"),
                Path.Combine(desktop, "ServerLibrary"),
                @"C:\Heerim\Library",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "서버라이브러리"),
            };

            foreach (var path in candidates)
            {
                if (Directory.Exists(path))
                    return path;
            }

            return "";
        }

        private void LoadCategoryTree()
        {
            CategoryTree.Items.Clear();

            try
            {
                var dirs = Directory.GetDirectories(_libraryRootPath)
                    .OrderBy(d => Path.GetFileName(d))
                    .ToList();

                foreach (var dir in dirs)
                {
                    string dirName = Path.GetFileName(dir);
                    if (dirName.StartsWith(".") || dirName == "obj" || dirName == "bin") continue;

                    var item = new TreeViewItem
                    {
                        Header = FormatCategoryName(dirName),
                        Tag = dir,
                        FontSize = 12,
                        Foreground = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#1D1D1F")),
                        Padding = new Thickness(4, 3, 4, 3)
                    };

                    // Check for subdirectories
                    try
                    {
                        var subDirs = Directory.GetDirectories(dir);
                        foreach (var subDir in subDirs.OrderBy(d => Path.GetFileName(d)))
                        {
                            string subDirName = Path.GetFileName(subDir);
                            var subItem = new TreeViewItem
                            {
                                Header = subDirName,
                                Tag = subDir,
                                FontSize = 11,
                                Foreground = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#636366")),
                                Padding = new Thickness(2, 2, 2, 2)
                            };
                            item.Items.Add(subItem);
                        }
                    }
                    catch { }

                    CategoryTree.Items.Add(item);
                }

                StatusText.Text = $"{dirs.Count}개 카테고리 발견";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"오류: {ex.Message}";
            }
        }

        private string FormatCategoryName(string dirName)
        {
            // Remove numeric prefix like "011_" for display
            if (dirName.Length > 4 && dirName[3] == '_' && char.IsDigit(dirName[0]))
            {
                return dirName.Substring(4);
            }
            return dirName;
        }

        private void CategoryTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (CategoryTree.SelectedItem is TreeViewItem selectedItem && selectedItem.Tag is string folderPath)
            {
                LoadFamiliesFromFolder(folderPath);
            }
        }

        private void LoadFamiliesFromFolder(string folderPath)
        {
            _allFamilies.Clear();
            FamilyGallery.Children.Clear();
            _selectedFamily = null;
            LoadButton.IsEnabled = false;

            try
            {
                var rfaFiles = Directory.GetFiles(folderPath, "*.rfa").OrderBy(f => f).ToList();

                foreach (var rfaFile in rfaFiles)
                {
                    string baseName = Path.GetFileNameWithoutExtension(rfaFile);
                    string pngPath = Path.Combine(folderPath, baseName + ".png");
                    string xmlPath = Path.Combine(folderPath, baseName + ".xml");

                    var familyItem = new LibraryFamilyItem
                    {
                        Name = baseName,
                        RfaPath = rfaFile,
                        PngPath = File.Exists(pngPath) ? pngPath : "",
                        XmlPath = File.Exists(xmlPath) ? xmlPath : "",
                        CategoryFolder = Path.GetFileName(folderPath)
                    };

                    // Load thumbnail
                    if (!string.IsNullOrEmpty(familyItem.PngPath))
                    {
                        try
                        {
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.UriSource = new Uri(familyItem.PngPath);
                            bitmap.DecodePixelWidth = 120;
                            bitmap.EndInit();
                            bitmap.Freeze();
                            familyItem.Thumbnail = bitmap;
                        }
                        catch { }
                    }

                    _allFamilies.Add(familyItem);
                }

                EmptyState.Visibility = rfaFiles.Count == 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

                ApplyFilter();
                ResultCountText.Text = $"{_allFamilies.Count}개 항목";
                StatusText.Text = $"{Path.GetFileName(folderPath)} - {rfaFiles.Count}개 패밀리";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"오류: {ex.Message}";
            }
        }

        private void ApplyFilter()
        {
            string query = SearchBox.Text?.Trim().ToLower() ?? "";

            _displayedFamilies = string.IsNullOrEmpty(query)
                ? _allFamilies.ToList()
                : _allFamilies.Where(f => f.Name.ToLower().Contains(query)).ToList();

            FamilyGallery.Children.Clear();

            foreach (var family in _displayedFamilies)
            {
                var card = CreateFamilyCard(family);
                FamilyGallery.Children.Add(card);
            }

            EmptyState.Visibility = _displayedFamilies.Count == 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            ResultCountText.Text = $"{_displayedFamilies.Count}개 항목";
        }

        private Border CreateFamilyCard(LibraryFamilyItem family)
        {
            // Thumbnail Image
            var image = new System.Windows.Controls.Image
            {
                Stretch = Stretch.Uniform,
                Width = 90,
                Height = 90,
                Margin = new Thickness(6)
            };

            if (family.Thumbnail != null)
                image.Source = family.Thumbnail;

            // Name Label
            var nameText = new TextBlock
            {
                Text = FormatFamilyDisplayName(family.Name),
                FontSize = 10,
                Foreground = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#424245")),
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 28,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(4, 0, 4, 4)
            };
            nameText.ToolTip = family.Name;

            var stack = new StackPanel();
            stack.Children.Add(image);
            stack.Children.Add(nameText);

            var card = new Border
            {
                Width = 120,
                Background = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#E5E5EA")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(4),
                Padding = new Thickness(2),
                Cursor = Cursors.Hand,
                Tag = family,
                Child = stack
            };

            card.MouseLeftButtonDown += OnFamilyCardClick;
            card.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2)
                {
                    OnLoadClick(null, null);
                }
            };

            return card;
        }

        private string FormatFamilyDisplayName(string name)
        {
            // Remove numeric prefix like "0001_M_DR_W_"
            if (name.Length > 4 && name[4] == '_')
            {
                string afterNum = name.Substring(5);
                // Remove category prefix like "M_DR_W_"
                var parts = afterNum.Split('_');
                if (parts.Length >= 4)
                {
                    return string.Join("_", parts.Skip(3));
                }
                return afterNum;
            }
            return name;
        }

        private void OnFamilyCardClick(object sender, MouseButtonEventArgs e)
        {
            // Deselect previous
            foreach (Border child in FamilyGallery.Children)
            {
                child.BorderBrush = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#E5E5EA"));
                child.BorderThickness = new Thickness(1);
                child.Background = new SolidColorBrush(Colors.White);
            }

            // Select clicked card
            if (sender is Border card && card.Tag is LibraryFamilyItem family)
            {
                card.BorderBrush = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#007AFF"));
                card.BorderThickness = new Thickness(2);
                card.Background = new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString("#EBF5FF"));
                _selectedFamily = family;
                LoadButton.IsEnabled = true;
                StatusText.Text = $"선택됨: {family.Name}";
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;

            ApplyFilter();
        }

        private void OnLoadClick(object? sender, RoutedEventArgs? e)
        {
            if (_selectedFamily == null)
            {
                StatusText.Text = "먼저 패밀리를 선택해주세요.";
                return;
            }

            if (!File.Exists(_selectedFamily.RfaPath))
            {
                StatusText.Text = $"파일을 찾을 수 없습니다: {_selectedFamily.RfaPath}";
                return;
            }

            try
            {
                StatusText.Text = $"로딩 중: {_selectedFamily.Name}...";

                using (var trans = new Transaction(_doc, "Load Family from Library"))
                {
                    trans.Start();

                    Family? loadedFamily = null;
                    bool success = _doc.LoadFamily(_selectedFamily.RfaPath, out loadedFamily);

                    if (success && loadedFamily != null)
                    {
                        trans.Commit();
                        StatusText.Text = $"✅ 로드 완료: {loadedFamily.Name} — Smart Assist에서 새로고침하면 사용 가능합니다.";

                        Autodesk.Revit.UI.TaskDialog.Show("로드 완료",
                            $"'{loadedFamily.Name}' 패밀리가 현재 프로젝트에 성공적으로 로드되었습니다.\n\n" +
                            "Smart Assist의 새로고침(🔄) 버튼을 누르면 즉시 사용할 수 있습니다.");
                    }
                    else
                    {
                        trans.RollBack();
                        StatusText.Text = $"⚠️ '{_selectedFamily.Name}' - 이미 로드되어 있거나 로드에 실패했습니다.";

                        Autodesk.Revit.UI.TaskDialog.Show("알림",
                            $"'{_selectedFamily.Name}' 패밀리가 이미 프로젝트에 있거나 로드에 실패했습니다.\n\n" +
                            "이미 로드된 경우 Smart Assist에서 바로 검색해 보세요.");
                    }
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"❌ 오류: {ex.Message}";
            }
        }
    }
}
