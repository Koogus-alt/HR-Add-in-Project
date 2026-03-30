using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.DB;

namespace Heerim_SheetsAndView
{
    public partial class MainView : Window
    {
        public MainViewModel ViewModel { get; set; }

        public MainView()
        {
            InitializeComponent();
            ViewModel = new MainViewModel();
            this.DataContext = ViewModel;
            ViewModel.RequestHide += () => this.Hide();
            ViewModel.RequestShow += () => this.ShowDialog();
        }

        private void OnApplyClick(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<ViewItem> ViewItems { get; set; }
        public ObservableCollection<SheetItem> SheetItems { get; set; }
        public ObservableCollection<LayoutPoint> LayoutPoints { get; set; }
        
        public event Action RequestHide;
        public event Action RequestShow;
        private Autodesk.Revit.UI.UIDocument _uiDoc;
        
        public List<string> Levels { get; set; }
        public List<string> ViewTemplates { get; set; }
        public List<string> ScopeBoxes { get; set; }
        public List<string> TitleBlocks { get; set; }

        private string _selectedTitleBlock;
        public string SelectedTitleBlock
        {
            get => _selectedTitleBlock;
            set { _selectedTitleBlock = value; OnPropertyChanged(nameof(SelectedTitleBlock)); }
        }

        private string _selectedViewTemplate;
        public string SelectedViewTemplate
        {
            get => _selectedViewTemplate;
            set { _selectedViewTemplate = value; OnPropertyChanged(nameof(SelectedViewTemplate)); }
        }

        private string _selectedScopeBox;
        public string SelectedScopeBox
        {
            get => _selectedScopeBox;
            set { _selectedScopeBox = value; OnPropertyChanged(nameof(SelectedScopeBox)); }
        }

        public ObservableCollection<string> Categories { get; set; } = new ObservableCollection<string> { "Floor Plan", "Ceiling Plan", "Section" };
        public ICommand ImportExcelCommand { get; }
        public ICommand ExportExcelCommand { get; }
        public ICommand AddViewCommand { get; }
        public ICommand DeleteViewCommand { get; }
        public ICommand DrawBoxCommand { get; }

        public MainViewModel()
        {
            ViewItems = new ObservableCollection<ViewItem>();
            SheetItems = new ObservableCollection<SheetItem>();
            LayoutPoints = new ObservableCollection<LayoutPoint>();
            
            ImportExcelCommand = new RelayCommand(OnImportExcel);
            ExportExcelCommand = new RelayCommand(OnExportExcel);
            AddViewCommand = new RelayCommand(OnAddView);
            DeleteViewCommand = new RelayCommand(OnDeleteView);
            DrawBoxCommand = new RelayCommand(OnDrawBox);

            // Generate 10x10 Grid Points
            for (int i = 0; i < 100; i++)
            {
                LayoutPoints.Add(new LayoutPoint { Number = i });
            }

            Levels = new List<string>();
            ViewTemplates = new List<string>();
            ScopeBoxes = new List<string>();
            TitleBlocks = new List<string>();
        }

        public void LoadData(Autodesk.Revit.UI.UIDocument uiDoc)
        {
            _uiDoc = uiDoc;
            Document doc = _uiDoc.Document;

            // Populate Dropdowns
            Levels = RevitDataManager.GetLevels(doc).Select(l => l.Name).ToList(); OnPropertyChanged(nameof(Levels));
            
            var templates = RevitDataManager.GetViewTemplates(doc).Select(t => t.Name).ToList();
            templates.Insert(0, "(None)");
            ViewTemplates = templates; OnPropertyChanged(nameof(ViewTemplates));
            
            var boxes = RevitDataManager.GetScopeBoxes(doc).Select(s => s.Name).ToList();
            boxes.Insert(0, "(None)");
            ScopeBoxes = boxes; OnPropertyChanged(nameof(ScopeBoxes));
            
            TitleBlocks = RevitDataManager.GetTitleBlocks(doc).Select(t => t.Name).ToList(); OnPropertyChanged(nameof(TitleBlocks));

            // Default Selections
            SelectedViewTemplate = "(None)";
            SelectedScopeBox = "(None)";
            if (TitleBlocks.Any()) SelectedTitleBlock = TitleBlocks.FirstOrDefault();

            // Populate initial lists
            ViewItems.Clear();
            foreach (var level in RevitDataManager.GetLevels(doc))
            {
                ViewItems.Add(new ViewItem 
                { 
                    LevelName = level.Name, 
                    ViewName = level.Name + " 평면도", 
                    IsSelected = false,
                    SelectedScopeBox = "(None)",
                    SelectedViewTemplate = "(None)"
                });
            }

            SheetItems.Clear();
            foreach (var sheet in RevitDataManager.GetSheets(doc))
            {
                SheetItems.Add(new SheetItem { SheetNumber = sheet.SheetNumber, SheetName = sheet.Name, IsSelected = false });
            }
        }

        private void OnImportExcel(object obj)
        {
            var items = ExcelManager.ImportFromCsv();
            if (items.Any())
            {
                SheetItems.Clear();
                foreach (var item in items) SheetItems.Add(item);
            }
        }

        private void OnExportExcel(object obj)
        {
            ExcelManager.ExportToCsv(SheetItems);
        }

        private void OnAddView(object obj)
        {
            var firstLevel = Levels?.FirstOrDefault() ?? "Level 1";
            ViewItems.Add(new ViewItem 
            { 
                LevelName = firstLevel, 
                ViewName = "New View", 
                IsSelected = true,
                SelectedScopeBox = "(None)",
                SelectedViewTemplate = "(None)"
            });
        }

        private void OnDeleteView(object obj)
        {
            var selectedItems = ViewItems.Where(v => v.IsSelected).ToList();
            if (!selectedItems.Any()) return;
            foreach (var item in selectedItems) ViewItems.Remove(item);
        }

        private void OnDrawBox(object obj)
        {
            if (obj is ViewItem item)
            {
                RequestHide?.Invoke();
                try
                {
                    var pickedBox = _uiDoc.Selection.PickBox(Autodesk.Revit.UI.Selection.PickBoxStyle.Directional, "크롭 영역을 드래그하세요.");
                    item.UserCropBox = new BoundingBoxXYZ { Min = pickedBox.Min, Max = pickedBox.Max };
                    item.HasCropBox = true;
                    DrawDetailLinesBox(_uiDoc.Document, pickedBox.Min, pickedBox.Max);
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { }
                catch (Exception ex)
                {
                    Autodesk.Revit.UI.TaskDialog.Show("Error", ex.Message);
                }
                finally
                {
                    RequestShow?.Invoke();
                }
            }
        }

        private void DrawDetailLinesBox(Document doc, XYZ min, XYZ max)
        {
            using (Transaction trans = new Transaction(doc, "크롭 영역 표시"))
            {
                trans.Start();
                try
                {
                    View activeView = doc.ActiveView;
                    XYZ pt1 = new XYZ(min.X, min.Y, 0);
                    XYZ pt2 = new XYZ(max.X, min.Y, 0);
                    XYZ pt3 = new XYZ(max.X, max.Y, 0);
                    XYZ pt4 = new XYZ(min.X, max.Y, 0);

                    Line line1 = Line.CreateBound(pt1, pt2);
                    Line line2 = Line.CreateBound(pt2, pt3);
                    Line line3 = Line.CreateBound(pt3, pt4);
                    Line line4 = Line.CreateBound(pt4, pt1);

                    doc.Create.NewDetailCurve(activeView, line1);
                    doc.Create.NewDetailCurve(activeView, line2);
                    doc.Create.NewDetailCurve(activeView, line3);
                    doc.Create.NewDetailCurve(activeView, line4);

                    trans.Commit();
                }
                catch { trans.RollBack(); }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ViewItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }

        private bool _hasCropBox;
        public bool HasCropBox
        {
            get => _hasCropBox;
            set 
            { 
                _hasCropBox = value; 
                OnPropertyChanged(nameof(HasCropBox)); 
                OnPropertyChanged(nameof(CropButtonText));
            }
        }
        
        public string CropButtonText => HasCropBox ? "✅ 지정됨" : "🔲 영역 지정";
        
        public BoundingBoxXYZ UserCropBox { get; set; }

        private string _category = "Floor Plan";
        public string Category
        {
            get => _category;
            set { _category = value; OnPropertyChanged(nameof(Category)); }
        }

        private string _levelName;
        public string LevelName
        {
            get => _levelName;
            set { _levelName = value; OnPropertyChanged(nameof(LevelName)); }
        }

        private string _viewName;
        
        private int _indentLevel;
        public int IndentLevel { get { return _indentLevel; } set { _indentLevel = value; OnPropertyChanged("IndentLevel"); } }
        private bool _isDependentView;
        public bool IsDependentView { get { return _isDependentView; } set { _isDependentView = value; OnPropertyChanged("IsDependentView"); } }

        public string ViewName
        {
            get => _viewName;
            set { _viewName = value; OnPropertyChanged(nameof(ViewName)); }
        }

        private string _selectedScopeBox = "(None)";
        public string SelectedScopeBox
        {
            get => _selectedScopeBox;
            set { _selectedScopeBox = value; OnPropertyChanged(nameof(SelectedScopeBox)); }
        }

        private string _selectedViewTemplate = "(None)";
        public string SelectedViewTemplate
        {
            get => _selectedViewTemplate;
            set { _selectedViewTemplate = value; OnPropertyChanged(nameof(SelectedViewTemplate)); }
        }

        private int _count = 1;
        public int Count
        {
            get => _count;
            set { _count = value; OnPropertyChanged(nameof(Count)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class SheetItem
    {
        public bool IsSelected { get; set; }
        public string SheetNumber { get; set; }
        public string SheetName { get; set; }
    }

    public class LayoutPoint : INotifyPropertyChanged
    {
        private bool _isSelected;
        public int Number { get; set; }
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
