using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Heerim_SmartAssist
{
    public partial class PresetListWindow : Window, INotifyPropertyChanged
    {
        private SmartAssistItem _masterItem;
        private FamilyBrowserView _mainView;
        public ObservableCollection<SmartAssistItem> FilteredPresets { get; set; }

        private bool _isEditMode;
        public bool IsEditMode
        {
            get => _isEditMode;
            set
            {
                if (_isEditMode != value)
                {
                    _isEditMode = value;
                    OnPropertyChanged();
                }
            }
        }

        public PresetListWindow(SmartAssistItem masterItem, FamilyBrowserView mainView)
        {
            InitializeComponent();
            _masterItem = masterItem;
            _mainView = mainView;
            ItemNameText.Text = masterItem.Name;
            ItemPreviewImage.Source = masterItem.Image;

            this.DataContext = this;
            
            // v150: Ensure window is activated and focused to capture ESC key
            this.Loaded += async (s, e) => {
                await System.Threading.Tasks.Task.Delay(50); // Slight delay to ensure Revit has handed over focus
                this.Activate();
                this.Focus();
                LoadPresets();
            };
        }

        // v139: Allow external refresh
        public void RefreshPresets()
        {
            Dispatcher.Invoke(() => LoadPresets());
        }

        private void LoadPresets()
        {
            // v131: Show all presets that share the same SymbolId (or name/category if SymbolId is missing)
            var presets = _mainView.FavoritesItems.Where(x => 
                x.IsPreset && 
                (x.SymbolId != null && _masterItem.SymbolId != null ? x.SymbolId == _masterItem.SymbolId : x.Name == _masterItem.Name && x.CategoryName == _masterItem.CategoryName)
            ).ToList();

            // v138: Check active state against global updater
            var activeParams = MyApplication.ParameterUpdater?.ActiveParameters;
            var activeSymbolId = MyApplication.ParameterUpdater?.ActiveSymbolId;

            foreach (var preset in presets)
            {
                // v152: Prioritize referential matching (actual selected object)
                if (_mainView.ActivePreset != null)
                {
                    preset.IsActive = (preset == _mainView.ActivePreset);
                }
                else
                {
                    preset.IsActive = false;
                    // v138 fallback: Check active parameters if no referential match (e.g. initial load)
                    if (preset.SymbolId != null && preset.SymbolId == activeSymbolId && activeParams != null)
                    {
                        bool match = preset.Parameters.Count == activeParams.Count;
                        if (match)
                        {
                            foreach (var kvp in preset.Parameters)
                            {
                                if (!activeParams.TryGetValue(kvp.Key, out string val) || val != kvp.Value)
                                {
                                    match = false;
                                    break;
                                }
                            }
                        }
                        preset.IsActive = match;
                    }
                }
            }

            FilteredPresets = new ObservableCollection<SmartAssistItem>(presets);
            PresetsListBox.ItemsSource = FilteredPresets;
        }

        private void OnAddPresetClick(object sender, RoutedEventArgs e)
        {
            // v175: Ensure Revit is not blocking the external event with a queued drawing command
            _mainView.CancelRevitCommandSafely(() => {
                _mainView.TriggerParameterFetchForNewPreset(_masterItem);
            });
        }

        private void OnEditPresetClick(object sender, RoutedEventArgs e)
        {
            var item = PresetsListBox.SelectedItem as SmartAssistItem;
            if (sender is Button btn && btn.DataContext is SmartAssistItem clickedItem) item = clickedItem;

            if (item != null)
            {
                // v175: Ensure Revit is not blocking the external event
                _mainView.CancelRevitCommandSafely(() => {
                    _mainView.TriggerParameterFetchForExistingPreset(item);
                });
            }
        }

        // v139: Double click triggers placement AND closes window
        private void OnPresetsListBoxDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var item = PresetsListBox.SelectedItem as SmartAssistItem;
            if (item != null)
            {
                _mainView.StartPlacementWithItem(item);
                this.Close();
            }
        }

        private void OnDeleteIndividualPresetClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is SmartAssistItem item)
            {
                if (MessageBox.Show($"'{item.DisplayName}' 프리셋을 삭제하시겠습니까?", "삭제 확인", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    _mainView.DeletePreset(item); // v160: Permanent deletion
                    FilteredPresets.Remove(item);
                }
            }
        }

        private void OnDeletePresetClick(object sender, RoutedEventArgs e)
        {
            // Toggle Edit Mode instead of immediate delete
            IsEditMode = !IsEditMode;
            if (!IsEditMode)
            {
                // Clear selections when exiting edit mode
                foreach (var item in FilteredPresets) item.IsChecked = false;
            }
        }

        private void OnSelectAllClick(object sender, RoutedEventArgs e)
        {
            foreach (var item in FilteredPresets) item.IsChecked = true;
        }

        private void OnDeselectAllClick(object sender, RoutedEventArgs e)
        {
            foreach (var item in FilteredPresets) item.IsChecked = false;
        }

        private void OnDeleteSelectedClick(object sender, RoutedEventArgs e)
        {
            var selectedItems = FilteredPresets.Where(x => x.IsChecked).ToList();
            if (selectedItems.Count == 0) return;

            if (MessageBox.Show($"선택한 {selectedItems.Count}개의 프리셋을 삭제하시겠습니까?", "선택 삭제 확인", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                foreach (var item in selectedItems)
                {
                    _mainView.DeletePreset(item); // v160: Permanent deletion
                    FilteredPresets.Remove(item);
                }
                IsEditMode = false;
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                this.Close();
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
