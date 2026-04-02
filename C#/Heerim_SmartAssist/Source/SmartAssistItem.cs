using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace Heerim_SmartAssist
{
    public class ParameterPill
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public bool IsType { get; set; }
    }

    public class SmartAssistItem : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public ImageSource Image { get; set; } 
        public Autodesk.Revit.DB.ElementId SymbolId { get; set; }
        public string CategoryName { get; set; }
        public string ItemType { get; set; } // "Family" or "Annotate"
        public bool IsPreset { get; set; } // v115: True if this is a custom preset, False if master item
        
        private string _customName;
        public string CustomName
        {
            get => _customName;
            set
            {
                if (_customName != value)
                {
                    _customName = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive != value)
                {
                    _isActive = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _activePresetDisplayName;
        public string ActivePresetDisplayName
        {
            get => _activePresetDisplayName;
            set
            {
                if (_activePresetDisplayName != value)
                {
                    _activePresetDisplayName = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasActivePreset));
                }
            }
        }

        public bool HasActivePreset => !string.IsNullOrEmpty(ActivePresetDisplayName);

        // v97: Store parameter presets
        public System.Collections.Generic.Dictionary<string, string> Parameters { get; set; } = new System.Collections.Generic.Dictionary<string, string>();

        public bool HasParameters => Parameters != null && Parameters.Count > 0;
        public string ParametersTooltip
        {
            get
            {
                if (!HasParameters) return "변경 없음";
                var lines = new System.Collections.Generic.List<string>();
                foreach(var kvp in Parameters) lines.Add($"{kvp.Key}: {kvp.Value}");
                return string.Join("\n", lines);
            }
        }

        public List<ParameterPill> ParameterPills 
        {
            get 
            {
                var list = new List<ParameterPill>();
                if (!HasParameters) return list;
                foreach(var kvp in Parameters) 
                {
                    bool isType = kvp.Key.EndsWith("|Type");
                    string name = kvp.Key.Replace("|Type", "").Replace("|Instance", "");
                    string displayValue = kvp.Value;
                    
                    if (name == "_SmartAssist_CompoundStructure" || name == "구조" || name == "복합 구조") 
                    {
                        name = "복합 구조";
                        if (displayValue.Contains(","))
                        {
                            displayValue = "사용자 지정 변경됨";
                        }
                    }
                    
                    list.Add(new ParameterPill { Name = name, Value = displayValue, IsType = isType });
                }
                return list;
            }
        }
        
        public void NotifyParametersChanged()
        {
            OnPropertyChanged(nameof(Parameters));
            OnPropertyChanged(nameof(HasParameters));
            OnPropertyChanged(nameof(ParametersTooltip));
            OnPropertyChanged(nameof(ParameterPills));
            OnPropertyChanged(nameof(ParametersDisplay));
        }

        public string ParametersDisplay => (IsPreset && HasParameters) ? "[P]" : "";

        private bool _isFavorite;
        public bool IsFavorite
        {
            get { return _isFavorite; }
            set
            {
                if (_isFavorite != value)
                {
                    _isFavorite = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(GroupHeader));
                    OnPropertyChanged(nameof(GroupOrder));
                }
            }
        }

        public void RefreshGroupProperties()
        {
            OnPropertyChanged(nameof(GroupHeader));
            OnPropertyChanged(nameof(GroupOrder));
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public static int FavoritesSortMode { get; set; } = 0; // 0: None, 1: Category Top, 2: Favorites Header

        public string GroupHeader
        {
            get
            {
                if (FavoritesSortMode == 2 && IsFavorite && ItemType == "Family")
                    return "★ 즐겨찾기";
                if (FavoritesSortMode == 2 && IsFavorite && ItemType == "Annotate")
                    return "★ 즐겨찾기"; // Can separate if needed, but they are in different tabs anyway
                return CategoryName;
            }
        }

        public string DisplayName => string.IsNullOrEmpty(CustomName) ? Name : CustomName;

        public int GroupOrder => (FavoritesSortMode == 2 && IsFavorite) ? 0 : 1;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
