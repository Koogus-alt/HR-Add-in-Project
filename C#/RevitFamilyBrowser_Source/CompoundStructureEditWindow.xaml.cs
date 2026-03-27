using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace RevitFamilyBrowser
{
    public class FunctionEntry
    {
        public int Value { get; set; }
        public string Header { get; set; }
    }

    public class CompoundLayerEntry : INotifyPropertyChanged
    {
        private int _function = 1;
        public int Function { get => _function; set { _function = value; OnPropertyChanged(nameof(Function)); } }

        private long _materialId = -1;
        public long MaterialId { get => _materialId; set { _materialId = value; OnPropertyChanged(nameof(MaterialId)); } }

        private double _thicknessMM = 0.0;
        public double ThicknessMM { get => _thicknessMM; set { _thicknessMM = value; OnPropertyChanged(nameof(ThicknessMM)); } }

        private bool _wraps = false;
        public bool Wraps { get => _wraps; set { _wraps = value; OnPropertyChanged(nameof(Wraps)); } }

        private bool _isStructural = false;
        public bool IsStructural { get => _isStructural; set { _isStructural = value; OnPropertyChanged(nameof(IsStructural)); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public partial class CompoundStructureEditWindow : Window, INotifyPropertyChanged
    {
        public ObservableCollection<CompoundLayerEntry> Layers { get; set; }
        public ObservableCollection<MaterialEntry> AvailableMaterials { get; set; }
        public ObservableCollection<FunctionEntry> FunctionOptions { get; set; }

        public string SerializedStructure { get; private set; }

        public CompoundStructureEditWindow(string initialData, List<MaterialEntry> materials)
        {
            InitializeComponent();

            FunctionOptions = new ObservableCollection<FunctionEntry>
            {
                new FunctionEntry { Value = 1, Header = "구조 [1]" },
                new FunctionEntry { Value = 2, Header = "하지 [2]" },
                new FunctionEntry { Value = 3, Header = "보온/공기층 [3]" },
                new FunctionEntry { Value = 4, Header = "마감 1 [4]" },
                new FunctionEntry { Value = 5, Header = "마감 2 [5]" },
                new FunctionEntry { Value = 0, Header = "멤브레인 층" }
            };

            var mats = new List<MaterialEntry> { new MaterialEntry { Id = -1, Name = "<카테고리별>", R = 255, G = 255, B = 255 } };
            if (materials != null) mats.AddRange(materials);
            AvailableMaterials = new ObservableCollection<MaterialEntry>(mats);
            Layers = new ObservableCollection<CompoundLayerEntry>();
            
            this.DataContext = this;

            ParseData(initialData);
        }

        private void ParseData(string data)
        {
            if (string.IsNullOrWhiteSpace(data)) return;
            string[] parts = data.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string p in parts)
            {
                string[] vals = p.Split(',');
                if (vals.Length >= 5)
                {
                    int.TryParse(vals[0], out int func);
                    long.TryParse(vals[1], out long mat);
                    double.TryParse(vals[2], out double thInternal);
                    bool.TryParse(vals[3], out bool wraps);
                    bool.TryParse(vals[4], out bool isStruct);

                    Layers.Add(new CompoundLayerEntry
                    {
                        Function = func,
                        MaterialId = mat,
                        ThicknessMM = thInternal * 304.8,
                        Wraps = wraps,
                        IsStructural = isStruct
                    });
                }
            }
        }

        private void OnInsertClick(object sender, RoutedEventArgs e)
        {
            int index = LayersGrid.SelectedIndex;
            var newLayer = new CompoundLayerEntry { Function = 1, MaterialId = -1, ThicknessMM = 100.0, Wraps = false, IsStructural = false };
            if (index >= 0 && index < Layers.Count)
                Layers.Insert(index, newLayer);
            else
                Layers.Add(newLayer);
        }

        private void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            int index = LayersGrid.SelectedIndex;
            if (index >= 0 && index < Layers.Count && Layers.Count > 1)
            {
                Layers.RemoveAt(index);
            }
            else if (Layers.Count == 1)
            {
                MessageBox.Show("복합 구조는 최소 1개의 레이어를 가져야 합니다.", "경고", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnUpClick(object sender, RoutedEventArgs e)
        {
            int index = LayersGrid.SelectedIndex;
            if (index > 0 && index < Layers.Count)
            {
                var item = Layers[index];
                Layers.RemoveAt(index);
                Layers.Insert(index - 1, item);
                LayersGrid.SelectedIndex = index - 1;
            }
        }

        private void OnDownClick(object sender, RoutedEventArgs e)
        {
            int index = LayersGrid.SelectedIndex;
            if (index >= 0 && index < Layers.Count - 1)
            {
                var item = Layers[index];
                Layers.RemoveAt(index);
                Layers.Insert(index + 1, item);
                LayersGrid.SelectedIndex = index + 1;
            }
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            SerializedStructure = string.Join(";", Layers.Select(l => 
                $"{l.Function},{l.MaterialId},{l.ThicknessMM / 304.8},{l.Wraps},{l.IsStructural}"
            ));
            this.DialogResult = true;
            this.Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
                OnCancelClick(null, null);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
