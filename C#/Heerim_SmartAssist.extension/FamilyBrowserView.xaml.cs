using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Windows.Threading;
// using System.Windows.Forms; // Removed to avoid ambiguity with Button

namespace Heerim_SmartAssist
{
    public partial class FamilyBrowserView : Page, IDockablePaneProvider
    {
        public static FamilyBrowserView Instance { get; private set; }

        public ObservableCollection<SmartAssistItem> FamilyItems { get; set; }
        public ObservableCollection<SmartAssistItem> AnnotateItems { get; set; }
        public ObservableCollection<SmartAssistItem> FavoritesItems { get; set; }

        public ObservableCollection<string> FamilyCategories { get; set; }
        public ObservableCollection<string> AnnotateCategories { get; set; }



        private ExternalEvent _externalEvent;
        private FamilyDataLoader _handler;
        private ExternalEvent _placementEvent;
        private FamilyPlacementHandler _placementHandler;
        
        private ParameterFetchHandler _paramFetcher;
        private ExternalEvent _paramFetcherEvent;

        private SmartAssistItem _selectedItem;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
        
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        private const uint WM_KEYDOWN = 0x0100;
        private const uint WM_KEYUP = 0x0101;

        private const int VK_ESCAPE = 0x1B;
        private DispatcherTimer _escTimer;
        private SmartAssistItem? _activePreset; // v148: Cache the active preset for persistence
        public SmartAssistItem? ActivePreset => _activePreset; // v152: Expose for referential matching in other windows
        private bool _isSwitching = false; // Guard to ignore synthetic ESC during element switching
        private System.Threading.CancellationTokenSource _cts = null; // v89 for interruption
        private bool _isProcessingClick = false; // v88 lock to sequence clicks
        private PresetListWindow? _activePresetWindow = null; // v139: Track open preset window

        private void FocusRevit()
        {
            if (MyApplication.Instance?.UIApp != null)
            {
                IntPtr revitHandle = MyApplication.Instance.UIApp.MainWindowHandle;
                if (revitHandle != IntPtr.Zero)
                {
                    SetForegroundWindow(revitHandle);
                }
            }
        }

        private void SendEscToRevit()
        {
            if (MyApplication.Instance?.UIApp != null)
            {
                IntPtr revitHandle = MyApplication.Instance.UIApp.MainWindowHandle;
                if (revitHandle != IntPtr.Zero)
                {
                    PostMessage(revitHandle, WM_KEYDOWN, (IntPtr)VK_ESCAPE, IntPtr.Zero);
                    PostMessage(revitHandle, WM_KEYUP, (IntPtr)VK_ESCAPE, IntPtr.Zero);
                }
            }
        }

        public FamilyBrowserView()
        {
            InitializeComponent();
            DataContext = this;
            Instance = this;

            _handler = new FamilyDataLoader(this);
            _externalEvent = ExternalEvent.Create(_handler);

            _placementHandler = new FamilyPlacementHandler();
            _placementEvent = ExternalEvent.Create(_placementHandler);

            _paramFetcher = new ParameterFetchHandler { View = this };
            _paramFetcherEvent = ExternalEvent.Create(_paramFetcher);

            FamilyItems = new ObservableCollection<SmartAssistItem>();
            AnnotateItems = new ObservableCollection<SmartAssistItem>();
            FavoritesItems = new ObservableCollection<SmartAssistItem>();
            
            FamilyCategories = new ObservableCollection<string> { "전체 카테고리" };
            AnnotateCategories = new ObservableCollection<string> { "전체 카테고리" };
            
            if (CategoryFamilyCombo != null)
            {
                CategoryFamilyCombo.ItemsSource = FamilyCategories;
                CategoryFamilyCombo.SelectedIndex = 0;
            }
            if (CategoryAnnotateCombo != null)
            {
                CategoryAnnotateCombo.ItemsSource = AnnotateCategories;
                CategoryAnnotateCombo.SelectedIndex = 0;
            }

            // Initialize Split Favorites CVS Filters (v31 ROBUST)
            var favAnnotateCVS = Resources["FavoriteAnnotateCVS"] as CollectionViewSource;
            if (favAnnotateCVS != null)
            {
                favAnnotateCVS.Filter += (s, args) => 
                {
                    var item = args.Item as SmartAssistItem;
                    bool isMatch = true;
                    if (!string.IsNullOrWhiteSpace(SearchBoxFavorites?.Text))
                    {
                        string query = SearchBoxFavorites.Text.ToLowerInvariant();
                        isMatch = item != null && (
                            (item.Name != null && item.Name.ToLowerInvariant().Contains(query)) ||
                            (item.CategoryName != null && item.CategoryName.ToLowerInvariant().Contains(query))
                        );
                    }
                    args.Accepted = item != null && item.ItemType == "Annotate" && !item.IsPreset && isMatch;
                };
            }

            var favFamilyCVS = Resources["FavoriteFamilyCVS"] as CollectionViewSource;
            if (favFamilyCVS != null)
            {
                favFamilyCVS.Filter += (s, args) => 
                {
                    var item = args.Item as SmartAssistItem;
                    bool isMatch = true;
                    if (!string.IsNullOrWhiteSpace(SearchBoxFavorites?.Text))
                    {
                        string query = SearchBoxFavorites.Text.ToLowerInvariant();
                        isMatch = item != null && (
                            (item.Name != null && item.Name.ToLowerInvariant().Contains(query)) ||
                            (item.CategoryName != null && item.CategoryName.ToLowerInvariant().Contains(query))
                        );
                    }
                    args.Accepted = item != null && item.ItemType == "Family" && !item.IsPreset && isMatch;
                };
            }

            // Trigger initial filtering
            SearchBoxFavorites_TextChanged(null, null);
            SearchBoxFamily_TextChanged(null, null);
            SearchBoxAnnotate_TextChanged(null, null);

            SetupSorting();


            this.PreviewKeyDown += OnPagePreviewKeyDown;
            
            // Initialize ESC polling timer
            _escTimer = new DispatcherTimer();
            _escTimer.Interval = TimeSpan.FromMilliseconds(10);
            _escTimer.Tick += OnEscTimerTick;

            this.Unloaded += (s, e) => _escTimer.Stop();

            // v123: Wire up events manually AFTER all initializations are complete
            // This prevents events from firing during InitializeComponent() and causing NullRef crashes
            if (MainTabControl != null)
            {
                MainTabControl.SelectionChanged += TabControl_SelectionChanged;
            }
            this.KeyDown += Page_KeyDown;
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            // Task 1: Auto-load list when page is loaded
            RefreshData();
        }

        public static void ShowFavorites()
        {
            if (Instance != null && Instance.MainTabControl != null)
            {
                // Favorites is usually the first tab (Index 0)
                Instance.MainTabControl.SelectedIndex = 0;
            }
        }

        private void OnEscTimerTick(object sender, EventArgs e)
        {
            // Ignore ESC polling while we're in the middle of switching elements
            if (_isSwitching) return;

            // Check if ESC is physically pressed (high bit of return value)
            if ((GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0)
            {
                if (_selectedItem != null)
                {
                    _selectedItem.IsSelected = false;
                    _selectedItem = null;
                }
                
                _escTimer.Stop();
                MyApplication.ParameterUpdater?.ClearActivePresets(); // v97: Clear presets on ESC
                Dispatcher.Invoke(() => ClearAllActiveFlags(false)); // v151: Revert persistence - ESC now clears the visual name/border as requested

                // Paced cancellation for maximum reliability
                System.Threading.Tasks.Task.Run(() =>
                {
                    try {
                        Dispatcher.Invoke(() => FocusRevit());
                        System.Threading.Thread.Sleep(80);
                        
                        // Paced ESC sequence with focus reinforcement
                        for(int i=0; i<3; i++) {
                            Dispatcher.Invoke(() => FocusRevit());
                            System.Threading.Thread.Sleep(20);
                            SendEscToRevit();
                            System.Threading.Thread.Sleep(100);
                        }
                        
                        Dispatcher.Invoke(() => PostModifyCommand());
                        
                        // v90: Force clear selection via API as a final failsafe
                        Dispatcher.Invoke(() =>
                        {
                            _placementHandler.SymbolId = null;
                            _placementEvent.Raise();
                        });
                        MyApplication.ParameterUpdater?.ClearActivePresets(); // v97: Clear presets on ESC sequence
                    } catch {}
                    
                    // Periodic check for ESC key release to prevent repeated triggers
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        DispatcherTimer waitReleaseTimer = new DispatcherTimer();
                        waitReleaseTimer.Interval = TimeSpan.FromMilliseconds(50);
                        waitReleaseTimer.Tick += (s, ev) =>
                        {
                            if ((GetAsyncKeyState(VK_ESCAPE) & 0x8000) == 0)
                            {
                                waitReleaseTimer.Stop();
                                _escTimer.Start();
                            }
                        };
                        waitReleaseTimer.Start();
                    }));
                });
            }
        }

        // v175: Dedicated safe cancellation for Option 1 buttons (+, Edit, Refresh)
        public void CancelRevitCommandSafely(Action onComplete)
        {
            if (_isSwitching) 
            {
                onComplete?.Invoke();
                return;
            }

            _isSwitching = true; // Blocks ESC timer from clearing UI flags
            _escTimer.Stop();

            System.Threading.Tasks.Task.Run(() =>
            {
                try {
                    Dispatcher.Invoke(() => FocusRevit());
                    System.Threading.Thread.Sleep(50);
                    
                    for (int i = 0; i < 3; i++)
                    {
                        Dispatcher.Invoke(() => FocusRevit());
                        System.Threading.Thread.Sleep(20);
                        SendEscToRevit();
                        System.Threading.Thread.Sleep(80);
                    }
                    
                    Dispatcher.Invoke(() => PostModifyCommand());
                    System.Threading.Thread.Sleep(80);
                } catch {}
                
                Dispatcher.Invoke(() =>
                {
                    _isSwitching = false;
                    _escTimer.Start();
                    onComplete?.Invoke();
                });
            });
        }

        private void PostModifyCommand()
        {
            if (MyApplication.Instance?.UIApp != null)
            {
                try {
                    RevitCommandId modifyId = RevitCommandId.LookupCommandId("ID_BUTTON_SELECT");
                    if (modifyId != null && MyApplication.Instance.UIApp.CanPostCommand(modifyId))
                    {
                        MyApplication.Instance.UIApp.PostCommand(modifyId);
                    }
                } catch {}
            }
        }

        private void OnPagePreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Ignore ESC if we're in middle of automated element switching
            if (_isSwitching) return;

            if (e.Key == System.Windows.Input.Key.Escape)
            {
                if (_selectedItem != null)
                {
                    _selectedItem.IsSelected = false;
                    _selectedItem = null;
                }
            }
        }

        private void SetupSorting()
        {
            ApplySortingConfiguration();
        }
        private void OnSettingsClick(object sender, RoutedEventArgs e)
        {
            if (MyApplication.Instance?.UIApp?.ActiveUIDocument?.Document is Document doc)
            {
                var settingsWindow = new SettingsWindow(SmartAssistItem.FavoritesSortMode, doc);
                settingsWindow.Owner = System.Windows.Window.GetWindow(this);
                
                if (settingsWindow.ShowDialog() == true)
                {
                    // If user saved settings, apply the new sort configuration and refresh
                    ApplySortingConfiguration();
                    
                    // Trigger property change on all items to update GroupHeader visually (for Mode 2)
                    foreach(var item in FamilyItems) item.RefreshGroupProperties();
                    foreach(var item in AnnotateItems) item.RefreshGroupProperties();

                    RefreshFavorites(); // Full UI refresh
                    RefreshData(); // Reload families to apply newly selected categories
                }
            }
            else
            {
                Autodesk.Revit.UI.TaskDialog.Show("오류", "현재 열려있는 레빗 프로젝트가 없습니다.");
            }
        }

        private void ApplySortingConfiguration()
        {
            var cvsList = new[] { "FamilyCVS", "AnnotateCVS" };
            foreach (var key in cvsList)
            {
                if (this.Resources[key] is CollectionViewSource cvs)
                {
                    cvs.SortDescriptions.Clear();
                    cvs.GroupDescriptions.Clear();

                    int mode = SmartAssistItem.FavoritesSortMode;

                    if (mode == 2)
                    {
                        // Option 3: Separate Header (Header name is controlled by GroupHeader property)
                        cvs.GroupDescriptions.Add(new PropertyGroupDescription("GroupHeader"));
                        
                        // Sort by GroupOrder ascending (0 for ★ 즐겨찾기, 1 for others) to force the Favorites block to the top.
                        // Then sort by GroupHeader ascending (to sort "가나다" normally)
                        cvs.SortDescriptions.Add(new System.ComponentModel.SortDescription("GroupOrder", System.ComponentModel.ListSortDirection.Ascending));
                        cvs.SortDescriptions.Add(new System.ComponentModel.SortDescription("GroupHeader", System.ComponentModel.ListSortDirection.Ascending));
                        cvs.SortDescriptions.Add(new System.ComponentModel.SortDescription("Name", System.ComponentModel.ListSortDirection.Ascending));
                    }
                    else if (mode == 1)
                    {
                        // Option 2: Top of Category
                        cvs.GroupDescriptions.Add(new PropertyGroupDescription("CategoryName"));
                        cvs.SortDescriptions.Add(new System.ComponentModel.SortDescription("CategoryName", System.ComponentModel.ListSortDirection.Ascending));
                        // Sort Favorites to top within category
                        cvs.SortDescriptions.Add(new System.ComponentModel.SortDescription("IsFavorite", System.ComponentModel.ListSortDirection.Descending)); 
                        cvs.SortDescriptions.Add(new System.ComponentModel.SortDescription("Name", System.ComponentModel.ListSortDirection.Ascending));
                    }
                    else
                    {
                        // Option 1: Natural sorting inside Category
                        cvs.GroupDescriptions.Add(new PropertyGroupDescription("CategoryName"));
                        cvs.SortDescriptions.Add(new System.ComponentModel.SortDescription("CategoryName", System.ComponentModel.ListSortDirection.Ascending));
                        cvs.SortDescriptions.Add(new System.ComponentModel.SortDescription("Name", System.ComponentModel.ListSortDirection.Ascending));
                    }
                }
            }
        }

        private void UpdateEmptyState()
        {
            if (FavoritesEmptyState != null && FavoritesContentPanel != null)
            {
                // v151: Strictly count only non-preset items (master families) to decide if favorites are "empty"
                bool hasVisibleItems = FavoritesItems != null && FavoritesItems.Count(x => !x.IsPreset) > 0;
                bool isEmpty = !hasVisibleItems;
                
                FavoritesEmptyState.Visibility = isEmpty ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
                FavoritesContentPanel.Visibility = isEmpty ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
            }
        }

        public void RefreshData()
        {
            if (_handler != null) _handler.Mode = LoaderMode.Load;
            _externalEvent.Raise();
        }

        private void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            CancelRevitCommandSafely(() => RefreshData());
        }



        private void OnModeClick(object sender, RoutedEventArgs e)
        {
            var isGrid = GridViewBtn.IsChecked == true;
            
            // Switch Template
            FamilyItemsControl.ItemTemplate = isGrid 
                ? (DataTemplate)Resources["FamilyItemTemplate"] 
                : (DataTemplate)Resources["FamilyListItemTemplate"];

            // Switch Panel
            if (isGrid)
            {
                var panelTemplate = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(WrapPanel)));
                FamilyItemsControl.ItemsPanel = panelTemplate;
            }
            else
            {
                var panelTemplate = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
                FamilyItemsControl.ItemsPanel = panelTemplate;
            }

            // Update Icon Colors (Green if active, Gray if inactive)
            GridIcon.Fill = new System.Windows.Media.SolidColorBrush(isGrid ? (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#34C759") : (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8E8E93"));
            ListIcon.Fill = new System.Windows.Media.SolidColorBrush(!isGrid ? (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#34C759") : (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8E8E93"));
        }

        private void OnFavModeClick(object sender, RoutedEventArgs e)
        {
            var isGrid = FavGridViewBtn.IsChecked == true;

            // Switch Template for Favorites Family List
            FavFamilyList.ItemTemplate = isGrid
                ? (DataTemplate)Resources["FamilyItemTemplate"]
                : (DataTemplate)Resources["FamilyListItemTemplate"];

            // Switch Panel
            if (isGrid)
            {
                var panelTemplate = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(WrapPanel)));
                FavFamilyList.ItemsPanel = panelTemplate;
            }
            else
            {
                var panelTemplate = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
                FavFamilyList.ItemsPanel = panelTemplate;
            }

            // Update Icon Colors
            FavGridIcon.Fill = new System.Windows.Media.SolidColorBrush(isGrid ? (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#34C759") : (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8E8E93"));
            FavListIcon.Fill = new System.Windows.Media.SolidColorBrush(!isGrid ? (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#34C759") : (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8E8E93"));
        }

        // Removed duplicate RefreshData and OnRefreshClick

        private void OnItemClick(object sender, System.Windows.RoutedEventArgs e)
        {
            var button = sender as Button;
            var item = button?.DataContext as SmartAssistItem;
            if (item != null) StartPlacementWithItem(item);
        }

        // v139: Expose placement logic for PresetListWindow
        public void StartPlacementWithItem(SmartAssistItem item)
        {
            // v89 Truly Interruptible: Cancel previous operations instantly
            try {
                _cts?.Cancel();
                _cts?.Dispose();
            } catch {}
            _cts = new System.Threading.CancellationTokenSource();
            var token = _cts.Token;

            if (item != null)
            {
                // Deselect previous item
                if (_selectedItem != null && _selectedItem != item)
                    _selectedItem.IsSelected = false;
                
                // Select new item and show border immediately
                item.IsSelected = true;
                _selectedItem = item;

                if (item.SymbolId != null)
                {
                    var symbolId = item.SymbolId;

                    // Block ESC timer/handlers while we send synthetic ESC to cancel current Revit command
                    _isSwitching = true;
                    _escTimer.Stop();

                    System.Threading.Tasks.Task.Run(() =>
                    {
                        try {
                            if (token.IsCancellationRequested) return;

                            // Focus Revit and cancel any active placement/dimensions
                            Dispatcher.Invoke(() => FocusRevit());
                            System.Threading.Thread.Sleep(80);
                            if (token.IsCancellationRequested) return;

                            // PACED ESC for sticky hosted elements (Doors/Windows) with focus reinforcement
                            for (int i = 0; i < 3; i++)
                            {
                                if (token.IsCancellationRequested) return;
                                Dispatcher.Invoke(() => FocusRevit());
                                System.Threading.Thread.Sleep(20);
                                SendEscToRevit();
                                System.Threading.Thread.Sleep(100); // Give Revit time to clear each sub-state
                            }

                            if (token.IsCancellationRequested) return;
                            Dispatcher.Invoke(() => PostModifyCommand());
                            System.Threading.Thread.Sleep(100); // Final settlement wait
                            
                            if (token.IsCancellationRequested) return;

                            // Raise placement for the new element on UI thread
                            Dispatcher.Invoke(() =>
                            {
                                if (token.IsCancellationRequested) return;
                                // CRITICAL v88: Force Revit focus one last time right before placement
                                // to ensure the cursor isn't "forbidden"
                                FocusRevit();
                                System.Threading.Thread.Sleep(50);
                                if (token.IsCancellationRequested) return;

                                // v97: Set active parameters for IUpdater
                                // v159: Call SetActiveItem for any preset OR if it has parameters
                                if (item.IsPreset || item.HasParameters)
                                {
                                    if (item.HasParameters)
                                    {
                                        // v167: Pass CustomName so duplicated Type borrows the exact Preset Name
                                        MyApplication.ParameterUpdater?.SetActivePreset(symbolId, item.Parameters, item.CustomName);
                                    }
                                    else
                                    {
                                        MyApplication.ParameterUpdater?.ClearActivePresets();
                                    }
                                    
                                    Dispatcher.Invoke(() => SetActiveItem(item)); // v138: Update UI active state
                                }
                                else
                                {
                                    MyApplication.ParameterUpdater?.ClearActivePresets();
                                    Dispatcher.Invoke(() => ClearAllActiveFlags()); // v138
                                }

                                _placementHandler.SymbolId = symbolId;
                                _placementEvent.Raise();
                            });

                            // Clear hardware state wait
                            System.Threading.Thread.Sleep(300);
                            if (token.IsCancellationRequested) return;
                            
                            Dispatcher.Invoke(() =>
                            {
                                _isSwitching = false;
                                _escTimer.Start();
                            });
                        } catch {}
                    }, token);
                }
            }
        }

        private void OnFavoriteClick(object sender, System.Windows.RoutedEventArgs e)
        {
            var toggleButton = sender as System.Windows.Controls.Primitives.ToggleButton;
            var item = toggleButton?.DataContext as SmartAssistItem;

            if (item != null)
            {
                if (item.IsFavorite)
                {
                    if (!FavoritesItems.Contains(item))
                    {
                        FavoritesItems.Add(item);
                    }
                }
                else
                {
                    if (FavoritesItems.Contains(item))
                    {
                        FavoritesItems.Remove(item);
                    }
                    else
                    {
                        // v115: If it's a master item, remove the "clean" favorite entry
                        var match = FavoritesItems.FirstOrDefault(x => x.Name == item.Name && x.CategoryName == item.CategoryName && !x.IsPreset);
                        if (match != null) FavoritesItems.Remove(match);
                    }
                }
            }
            
            RefreshViewsAndSaveState();
        }

        private void OnManagePresetClick(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var item = button?.DataContext as SmartAssistItem;

            if (item != null)
            {
                // v131: Open the new Preset List window instead of directly opening the editor
                _activePresetWindow = new PresetListWindow(item, this);
                _activePresetWindow.Owner = Window.GetWindow(this);
                _activePresetWindow.Closed += (s, ev) => _activePresetWindow = null; // v139: Clear ref on close
                _activePresetWindow.Show(); // v139: Use Show() instead of ShowDialog() to allow Revit interaction if needed, though usually Dialog is safer. 
                                            // USER wants it to stay open during modeling? 
                                            // Actually, ShowDialog blocks. Let's stick to Dialog for now but ensure it stays open during creation.
            }
        }

        // v131: Helper methods called from PresetListWindow
        public void TriggerParameterFetchForNewPreset(SmartAssistItem masterItem)
        {
            if (masterItem != null)
            {
                _paramFetcher.TargetItem = masterItem;
                _paramFetcherEvent.Raise();
            }
        }

        public void TriggerParameterFetchForExistingPreset(SmartAssistItem presetItem)
        {
            if (presetItem != null)
            {
                _paramFetcher.TargetItem = presetItem;
                _paramFetcherEvent.Raise();
            }
        }

        public void ShowParameterEditWindow(SmartAssistItem item, Dictionary<string, ParameterInfo> availableParams)
        {
            // v123: Calculate how many presets already exist for this master item to provide dynamic naming
            // v127: If we are already editing a preset, we don't need to increment the index for naming
            int presetCount = FavoritesItems.Count(x => x.SymbolId == item.SymbolId && x.IsPreset);
            int namingIndex = item.IsPreset ? Math.Max(0, presetCount - 1) : presetCount;
            
            var editWindow = new ParameterEditWindow(item, availableParams, namingIndex);
            
            // v166: Fix silent crash caused by Revit WPF hidden tooltips with Topmost=true
            Window owner = Window.GetWindow(this);
            var presetWin = Application.Current.Windows.OfType<PresetListWindow>().FirstOrDefault(w => w.IsVisible);
            if (presetWin != null)
            {
                owner = presetWin;
            }
            editWindow.Owner = owner;
            
            if (editWindow.ShowDialog() == true)
            {
                if (item.IsPreset)
                {
                    // v127: Update the existing preset in-place instead of cloning
                    item.CustomName = editWindow.CustomName;
                    item.Parameters = new Dictionary<string, string>(item.Parameters); // Ensure fresh copy if needed, but usually we just want to update values
                    // The parameters in 'item' were likely already modified by the dialog if it shares the reference, 
                    // but let's be explicit if ParameterEditWindow creates a new dict.
                    // Assuming ParameterEditWindow modifies the item's parameters or provides them back.
                    // Actually, let's look at how ParameterEditWindow works.
                }
                else
                {
                    // User saved the preset from a Master item. Clone the item as a new preset.
                    // v158: Use editWindow.SavedParameters instead of item.Parameters (which remains the clean master)
                    var clonedItem = new SmartAssistItem
                    {
                        Name = item.Name,
                        Image = item.Image,
                        SymbolId = item.SymbolId,
                        CategoryName = item.CategoryName,
                        ItemType = item.ItemType,
                        CustomName = editWindow.CustomName,
                        Parameters = editWindow.SavedParameters,
                        IsFavorite = true,
                        IsPreset = true
                    };
                    
                    // v129: Add to main collections so it persists after RefreshFavorites()
                    if (item.ItemType == "Family")
                    {
                        FamilyItems.Add(clonedItem);
                    }
                    else if (item.ItemType == "Annotate")
                    {
                        AnnotateItems.Add(clonedItem);
                    }
                    
                    // v123: Auto-favorite the Master item when a preset is created
                    item.IsFavorite = true;
                }

                // v131: Ensure favorites are refreshed so New presets appear immediately
                RefreshFavorites();
                RefreshViewsAndSaveState();

                // v139: Refresh the persistent preset list window
                _activePresetWindow?.RefreshPresets();
            }
        }

        public void RefreshViewsAndSaveState()
        {
            // Preserve scroll positions before refreshing CollectionViews
            // so that the list doesn't jump to the top
            var scrollViewers = new Dictionary<string, double>();
            foreach (var sv in FindVisualChildren<ScrollViewer>(this))
            {
                scrollViewers[sv.Name ?? sv.GetHashCode().ToString()] = sv.VerticalOffset;
            }

            // Refresh CVs
            var familyCVS = this.Resources["FamilyCVS"] as CollectionViewSource;
            familyCVS?.View?.Refresh();
            
            var annotateCVS = this.Resources["AnnotateCVS"] as CollectionViewSource;
            annotateCVS?.View?.Refresh();

            var favoritesCVS = this.Resources["FavoritesCVS"] as CollectionViewSource;
            favoritesCVS?.View?.Refresh();

            UpdateEmptyState();
            SaveFavorites();

            // Restore scroll positions after layout update
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                foreach (var sv in FindVisualChildren<ScrollViewer>(this))
                {
                    string key = sv.Name ?? sv.GetHashCode().ToString();
                    if (scrollViewers.TryGetValue(key, out double offset))
                    {
                        sv.ScrollToVerticalOffset(offset);
                    }
                }
            }));
        }



        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj == null) yield break;
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(depObj, i);
                if (child is T typedChild)
                    yield return typedChild;
                foreach (var childOfChild in FindVisualChildren<T>(child))
                    yield return childOfChild;
            }
        }

        private void SaveFavorites()
        {
            try
            {
                var favoritesList = new List<object>();

                if (FavoritesItems != null)
                {
                    foreach (var item in FavoritesItems)
                    {
                        // Make unique key: Category::Name::CustomName
                        string uniqueKey = item.CategoryName + "::" + item.Name;
                        if (!string.IsNullOrEmpty(item.CustomName))
                        {
                            uniqueKey += "::" + item.CustomName;
                        }

                        var favObj = new
                        {
                            Key = uniqueKey,
                            Parameters = item.Parameters
                        };
                        favoritesList.Add(favObj);
                    }
                }

                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string folder = System.IO.Path.Combine(appDataPath, "Autodesk", "Revit", "Addins", "2026", "RevitFamilyBrowser");
                System.IO.Directory.CreateDirectory(folder);
                string favoritesFile = System.IO.Path.Combine(folder, "favorites_v2.json");

                string json = System.Text.Json.JsonSerializer.Serialize(favoritesList, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                System.IO.File.WriteAllText(favoritesFile, json);
            }
            catch (Exception)
            {
                // Optionally log error
            }
        }

        public void UpdateFamilyItems(IEnumerable<SmartAssistItem> items)
        {
            FamilyItems.Clear();
            var categories = new HashSet<string>();
            foreach (var item in items)
            {
                // v148: Restore active state if this master item matches the cached active preset
                if (_activePreset != null && !item.IsPreset)
                {
                    if (item.SymbolId != null && _activePreset.SymbolId != null && item.SymbolId.Compare(_activePreset.SymbolId) == 0)
                    {
                        item.IsActive = true;
                        item.ActivePresetDisplayName = _activePreset.DisplayName;
                    }
                }
                FamilyItems.Add(item);
                if (!string.IsNullOrEmpty(item.CategoryName))
                {
                    categories.Add(item.CategoryName);
                }
            }
            
            // Update Category Dropdown
            var selectedIdx = CategoryFamilyCombo?.SelectedIndex ?? 0;
            FamilyCategories.Clear();
            FamilyCategories.Add("전체 카테고리");
            foreach (var cat in categories.OrderBy(c => c))
            {
                FamilyCategories.Add(cat);
            }
            if (CategoryFamilyCombo != null)
            {
                CategoryFamilyCombo.SelectedIndex = Math.Min(selectedIdx, FamilyCategories.Count - 1);
            }

            RefreshFavorites();
        }

        public void UpdateAnnotateItems(IEnumerable<SmartAssistItem> items)
        {
            AnnotateItems.Clear();
            var categories = new HashSet<string>();
            foreach (var item in items)
            {
                // v148: Restore active state
                if (_activePreset != null && !item.IsPreset)
                {
                    if (item.SymbolId != null && _activePreset.SymbolId != null && item.SymbolId.Compare(_activePreset.SymbolId) == 0)
                    {
                        item.IsActive = true;
                        item.ActivePresetDisplayName = _activePreset.DisplayName;
                    }
                }
                AnnotateItems.Add(item);
                if (!string.IsNullOrEmpty(item.CategoryName))
                {
                    categories.Add(item.CategoryName);
                }
            }
            
            // Update Category Dropdown
            var selectedIdx = CategoryAnnotateCombo?.SelectedIndex ?? 0;
            AnnotateCategories.Clear();
            AnnotateCategories.Add("전체 카테고리");
            foreach (var cat in categories.OrderBy(c => c))
            {
                AnnotateCategories.Add(cat);
            }
            if (CategoryAnnotateCombo != null)
            {
                CategoryAnnotateCombo.SelectedIndex = Math.Min(selectedIdx, AnnotateCategories.Count - 1);
            }

            RefreshFavorites();
        }

        private void RefreshFavorites()
        {
            FavoritesItems.Clear();
            foreach (var item in AnnotateItems.Where(x => x.IsFavorite))
            {
                FavoritesItems.Add(item);
            }
            foreach (var item in FamilyItems.Where(x => x.IsFavorite))
            {
                FavoritesItems.Add(item);
            }

            // Refresh CVS
            var favAnnotateCVS = this.Resources["FavoriteAnnotateCVS"] as CollectionViewSource;
            favAnnotateCVS?.View?.Refresh();
            var favFamilyCVS = this.Resources["FavoriteFamilyCVS"] as CollectionViewSource;
            favFamilyCVS?.View?.Refresh();

            var familyCVS = this.Resources["FamilyCVS"] as CollectionViewSource;
            familyCVS?.View?.Refresh();
            var annotateCVS = this.Resources["AnnotateCVS"] as CollectionViewSource;
            annotateCVS?.View?.Refresh();

            UpdateEmptyState();
        }


        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = this as System.Windows.FrameworkElement;
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Right,
            };
        }

        private void SearchBoxFavorites_TextChanged(object sender, TextChangedEventArgs e)
        {
            string filterText = SearchBoxFavorites?.Text?.ToLowerInvariant() ?? "";

            System.Predicate<object> filter = (item) =>
            {
                if (!(item is SmartAssistItem sai)) return false;
                if (sai.IsPreset) return false; // v133: Hide presets from main list

                if (string.IsNullOrWhiteSpace(filterText)) return true;
                
                // v129: Search by DisplayName to catch renamed items
                bool matchName = sai.DisplayName != null && sai.DisplayName.ToLowerInvariant().Contains(filterText);
                bool matchCategory = sai.CategoryName != null && sai.CategoryName.ToLowerInvariant().Contains(filterText);
                return matchName || matchCategory;
            };

            var favoritesCVS = this.Resources["FavoritesCVS"] as CollectionViewSource;
            if (favoritesCVS?.View != null) favoritesCVS.View.Filter = filter;

            var favAnnotateCVS = this.Resources["FavoriteAnnotateCVS"] as CollectionViewSource;
            favAnnotateCVS?.View?.Refresh();

            var favFamilyCVS = this.Resources["FavoriteFamilyCVS"] as CollectionViewSource;
            favFamilyCVS?.View?.Refresh();
        }

        private void SearchBoxFamily_TextChanged(object sender, TextChangedEventArgs e)
        {
            string filterText = SearchBoxFamily?.Text?.ToLowerInvariant() ?? "";
            string selectedCategory = CategoryFamilyCombo?.SelectedItem as string;
            bool isAllCategories = string.IsNullOrEmpty(selectedCategory) || selectedCategory == "전체 카테고리";

            System.Predicate<object> filter = (item) =>
            {
                if (!(item is SmartAssistItem sai)) return false;

                // v129: Exclude presets from Family tab (only master items here)
                if (sai.IsPreset) return false;

                // v129: Search by DisplayName
                bool matchText = string.IsNullOrWhiteSpace(filterText) || 
                                 (sai.DisplayName != null && sai.DisplayName.ToLowerInvariant().Contains(filterText)) ||
                                 (sai.CategoryName != null && sai.CategoryName.ToLowerInvariant().Contains(filterText));
                bool matchCategory = isAllCategories || sai.CategoryName == selectedCategory;

                return matchText && matchCategory;
            };

            var familyCVS = this.Resources["FamilyCVS"] as CollectionViewSource;
            if (familyCVS?.View != null) familyCVS.View.Filter = filter;
        }

        private void CategoryFamilyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SearchBoxFamily_TextChanged(null, null);
        }

        private void SearchBoxAnnotate_TextChanged(object sender, TextChangedEventArgs e)
        {
            string filterText = SearchBoxAnnotate?.Text?.ToLowerInvariant() ?? "";
            string selectedCategory = CategoryAnnotateCombo?.SelectedItem as string;
            bool isAllCategories = string.IsNullOrEmpty(selectedCategory) || selectedCategory == "전체 카테고리";

            System.Predicate<object> filter = (item) =>
            {
                if (!(item is SmartAssistItem sai)) return false;

                // v129: Exclude presets from Annotate tab
                if (sai.IsPreset) return false;

                // v129: Search by DisplayName
                bool matchText = string.IsNullOrWhiteSpace(filterText) || 
                                 (sai.DisplayName != null && sai.DisplayName.ToLowerInvariant().Contains(filterText)) ||
                                 (sai.CategoryName != null && sai.CategoryName.ToLowerInvariant().Contains(filterText));
                bool matchCategory = isAllCategories || sai.CategoryName == selectedCategory;

                return matchText && matchCategory;
            };

            var annotateCVS = this.Resources["AnnotateCVS"] as CollectionViewSource;
            if (annotateCVS?.View != null) annotateCVS.View.Filter = filter;
        }

        private void CategoryAnnotateCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SearchBoxAnnotate_TextChanged(null, null);
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // v123 (v117 logic): Deselect on tab switch to prevent confusion
            if (e.Source is TabControl)
            {
                if (FamilyItems != null) foreach (var item in FamilyItems) item.IsSelected = false;
                if (AnnotateItems != null) foreach (var item in AnnotateItems) item.IsSelected = false;
                if (FavoritesItems != null) foreach (var item in FavoritesItems) item.IsSelected = false;
                
                // Set focus to page for KeyDown
                this.Focus();
            }
        }

        private void Page_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // v123 (v117 logic): Delete key removes from Favorites
            if (e.Key == System.Windows.Input.Key.Delete)
            {
                // Deletion only supported in Favorites tab
                if (MainTabControl.SelectedIndex == 0)
                {
                    var selectedItem = FavoritesItems.FirstOrDefault(x => x.IsSelected);
                    if (selectedItem != null)
                    {
                        // Simulate unstarring
                        selectedItem.IsFavorite = false;
                        OnFavoriteClick(null, null);
                    }
                }
            }
        }

        // v138: Active Preset UI Helpers
        private void ClearAllActiveFlags(bool preserveCache = false)
        {
            if (!preserveCache) _activePreset = null; // v148/v150: Clear cached state unless explicitly asked to preserve (like on Revit ESC)
            
            if (FamilyItems != null) foreach (var it in FamilyItems) { it.IsActive = false; it.ActivePresetDisplayName = ""; }
            if (AnnotateItems != null) foreach (var it in AnnotateItems) { it.IsActive = false; it.ActivePresetDisplayName = ""; }
            if (FavoritesItems != null) foreach (var it in FavoritesItems) { it.IsActive = false; it.ActivePresetDisplayName = ""; }
            
            if (ActivePresetStatusBar != null) ActivePresetStatusBar.Visibility = System.Windows.Visibility.Collapsed;
        }

        private void SetActiveItem(SmartAssistItem item)
        {
            ClearAllActiveFlags(false); // v150: Full reset when explicitly clicking a new item
            if (item != null)
            {
                item.IsActive = true;

                // v148: Cache the active state
                if (item.IsPreset)
                {
                    _activePreset = item;
                }
                else
                {
                    _activePreset = null; // Master item selected (Clear visual link)
                }

                // v143: Update all instances of the master item (e.g. in Favorites and Category tabs)
                if (item.IsPreset)
                {
                    var masters = FindAllMasterItems(item);
                    foreach (var m in masters)
                    {
                        m.ActivePresetDisplayName = item.DisplayName;
                        m.IsActive = true;
                    }
                }

                if (ActivePresetStatusBar != null)
                {
                    ActivePresetStatusBar.Visibility = System.Windows.Visibility.Visible;
                    ActivePresetNameText.Text = item.DisplayName;
                }

                // v145: Force UI refresh to ensure (Preset Name) labels appear immediately
                RefreshViewsAndSaveState();
            }
        }

        // v160: Centralized permanent deletion of presets
        public void DeletePreset(SmartAssistItem item)
        {
            if (item == null || !item.IsPreset) return;

            item.IsFavorite = false;
            FamilyItems.Remove(item);
            AnnotateItems.Remove(item);
            FavoritesItems.Remove(item);

            if (_activePreset == item) _activePreset = null;
            if (_selectedItem == item) _selectedItem = null;

            RefreshViewsAndSaveState();
        }

        // v143: Helper to find ALL master item instances for a given preset
        private System.Collections.Generic.List<SmartAssistItem> FindAllMasterItems(SmartAssistItem preset)
        {
            var results = new System.Collections.Generic.List<SmartAssistItem>();
            var allCollections = new[] { FamilyItems, AnnotateItems, FavoritesItems };
            
            foreach (var coll in allCollections)
            {
                if (coll == null) continue;
                var matches = coll.Where(i => !i.IsPreset && 
                                           ((i.SymbolId != null && preset.SymbolId != null) 
                                                ? i.SymbolId.Compare(preset.SymbolId) == 0 
                                                : (i.Name == preset.Name && i.CategoryName == preset.CategoryName)));
                results.AddRange(matches);
            }
            return results;
        }
    }
}
