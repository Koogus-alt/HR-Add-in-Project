# -*- coding: utf-8 -*-
from pyrevit import revit, DB, forms, script
import os
import sys
import json

# Load shared library
LIB_PATH = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "..", "lib"))
if LIB_PATH not in sys.path:
    sys.path.append(LIB_PATH)
import autojoin_logic
import clr
clr.AddReference("System")
import System

class CategoryOption(object):
    def __init__(self, name, is_selected):
        self._name = name
        self._is_selected = is_selected
        
    @property
    def Name(self): return self._name
        
    @property
    def IsSelected(self): return self._is_selected
        
    @IsSelected.setter
    def IsSelected(self, value): self._is_selected = value

class CategoryEditorWindow(forms.WPFWindow):
    def __init__(self, xaml_file_name, all_cats, checked_cats):
        forms.WPFWindow.__init__(self, xaml_file_name)
        self.options = [CategoryOption(c, c in checked_cats) for c in all_cats]
        self.CatListBox.ItemsSource = self.options
        self.confirmed = False
        self.filter_selected_only = False
        
    def _apply_filter(self):
        import System
        from System.Windows.Data import CollectionViewSource
        from System import Predicate
        
        view = CollectionViewSource.GetDefaultView(self.options)
        search_text = self.SearchBox.Text.lower() if hasattr(self, "SearchBox") else ""
        
        def filter_func(item):
            match_search = not search_text or search_text in item.Name.lower()
            match_sel = not self.filter_selected_only or item.IsSelected
            return match_search and match_sel
            
        view.Filter = Predicate[System.Object](filter_func)
        
    def SearchBox_TextChanged(self, sender, e):
        self._apply_filter()
        
    def FilterBtn_Click(self, sender, e):
        from System.Windows.Media import BrushConverter, Brushes
        self.filter_selected_only = not self.filter_selected_only
        
        if self.filter_selected_only:
            self.FilterBtn.Background = BrushConverter().ConvertFromString("#34C759")
            self.FilterBtn.Foreground = Brushes.White
        else:
            self.FilterBtn.Background = BrushConverter().ConvertFromString("#E5E5EA")
            self.FilterBtn.Foreground = Brushes.Black
            
        self._apply_filter()
        
    def CheckAll(self, sender, e):
        for opt in self.options: opt.IsSelected = True
        self.CatListBox.Items.Refresh()
        
    def UncheckAll(self, sender, e):
        for opt in self.options: opt.IsSelected = False
        self.CatListBox.Items.Refresh()
        
    def OKBtn(self, sender, e):
        self.confirmed = True
        self.Close()
        
    def CloseBtn(self, sender, e):
        self.Close()

class AutoJoinWindow(forms.WPFWindow):
    def __init__(self, xaml_file_name):
        forms.WPFWindow.__init__(self, xaml_file_name)
        self._setup_data()
        self.rules = []
        self._add_default_rules()

    def Window_MouseDown(self, sender, e):
        import System
        if e.ChangedButton == System.Windows.Input.MouseButton.Left:
            self.DragMove()

    def Window_KeyDown(self, sender, e):
        import System
        if e.Key == System.Windows.Input.Key.Escape:
            self.Close()
            
    def _refresh_ranks(self):
        for i, r in enumerate(self.rules):
            r["rank"] = i + 1
        self.RuleList.ItemsSource = None
        self.RuleList.ItemsSource = self.rules
        
    def Rank_LostFocus(self, sender, e):
        self._update_rank_from_sender(sender)
        
    def Rank_KeyDown(self, sender, e):
        import System
        if e.Key == System.Windows.Input.Key.Enter:
            self._update_rank_from_sender(sender)
            
    def _update_rank_from_sender(self, sender):
        try:
            new_text = sender.Text
            if new_text and new_text.isdigit():
                new_rank = int(new_text)
                target_idx = max(0, new_rank - 1)
                
                data_context = sender.DataContext
                if data_context:
                    cat_name = data_context.get("category")
                    
                    old_idx = -1
                    rule_to_move = None
                    for i, r in enumerate(self.rules):
                        if r.get("category") == cat_name:
                            old_idx = i
                            rule_to_move = r
                            break
                            
                    if old_idx != -1 and old_idx != target_idx:
                        self.rules.pop(old_idx)
                        target_idx = min(target_idx, len(self.rules))
                        self.rules.insert(target_idx, rule_to_move)
        except:
            pass
        self._refresh_ranks()

    # ----- DRAG & DROP LOGIC -----
    def RuleList_PreviewMouseLeftButtonDown(self, sender, e):
        import System
        from System.Windows import DependencyObject
        from System.Windows.Media import VisualTreeHelper
        from System.Windows.Controls import ListBoxItem
        
        # Check if the click was on the Drag Handle (TextBlock with Text "≡")
        if hasattr(e.OriginalSource, "Text") and e.OriginalSource.Text == "≡":
            self.dragStartPoint = e.GetPosition(None)
        else:
            self.dragStartPoint = None

    def RuleList_MouseMove(self, sender, e):
        import System
        from System.Windows import DragDrop, DragDropEffects, DataFormats
        from System.Windows.Input import MouseButtonState
        
        if e.LeftButton == MouseButtonState.Pressed and getattr(self, "dragStartPoint", None):
            pos = e.GetPosition(None)
            import math
            # Threshold to prevent accidental drags
            if math.hypot(pos.X - self.dragStartPoint.X, pos.Y - self.dragStartPoint.Y) > 5:
                listBoxItem = self._FindAncestor(e.OriginalSource, "ListBoxItem")
                if listBoxItem and listBoxItem.DataContext:
                    # Pass the string representation of the index
                    idx = self.rules.index(listBoxItem.DataContext)
                    DragDrop.DoDragDrop(listBoxItem, str(idx), DragDropEffects.Move)

    def _reset_listbox_borders(self):
        from System.Windows.Media import BrushConverter
        from System.Windows import Thickness
        bc = BrushConverter()
        for i in range(self.RuleList.Items.Count):
            lbi = self.RuleList.ItemContainerGenerator.ContainerFromIndex(i)
            if lbi:
                lbi.BorderBrush = bc.ConvertFromString("#F0F0F0")
                lbi.BorderThickness = Thickness(0,0,0,1)

    def RuleList_Drop(self, sender, e):
        import System
        from System.Windows import DragDropEffects, DataFormats
        
        self._reset_listbox_borders()
        
        if e.Data.GetDataPresent(DataFormats.StringFormat):
            idxStr = e.Data.GetData(DataFormats.StringFormat)
            try:
                draggedIdx = int(idxStr)
                droppedData = self.rules[draggedIdx]
                targetItem = self._FindAncestor(e.OriginalSource, "ListBoxItem")
                if targetItem:
                    targetIdx = getattr(self, "_current_drop_idx", None)
                    if targetIdx is None:
                        if targetItem.DataContext:
                            targetIdx = self.rules.index(targetItem.DataContext)
                        else:
                            targetIdx = len(self.rules)
                    
                    if targetIdx > draggedIdx:
                        targetIdx -= 1 # adjust shift forward since we are removing one from before
                        
                    if targetIdx != draggedIdx:
                        self.rules.remove(droppedData)
                        self.rules.insert(targetIdx, droppedData)
                        self._refresh_ranks()
            except:
                pass
        self.dragStartPoint = None
        self._current_drop_idx = None

    def RuleList_DragOver(self, sender, e):
        from System.Windows.Media import BrushConverter
        from System.Windows import Thickness
        targetItem = self._FindAncestor(e.OriginalSource, "ListBoxItem")
        self._reset_listbox_borders()
        
        if targetItem:
            pos = e.GetPosition(targetItem)
            idx = self.RuleList.ItemContainerGenerator.IndexFromContainer(targetItem)
            if pos.Y < targetItem.ActualHeight / 2:
                self._current_drop_idx = idx
                targetItem.BorderBrush = BrushConverter().ConvertFromString("#34C759")
                targetItem.BorderThickness = Thickness(0, 3, 0, 1)
            else:
                self._current_drop_idx = idx + 1
                targetItem.BorderBrush = BrushConverter().ConvertFromString("#34C759")
                targetItem.BorderThickness = Thickness(0, 0, 0, 3)

    def RuleList_DragLeave(self, sender, e):
        self._reset_listbox_borders()
        self._current_drop_idx = None

    def RuleList_MouseUp(self, sender, e):
        self._reset_listbox_borders()
        self.dragStartPoint = None
        self._current_drop_idx = None

    def _FindAncestor(self, current, targetTypeName):
        from System.Windows.Media import VisualTreeHelper
        while current is not None:
            if current.GetType().Name == targetTypeName:
                return current
            current = VisualTreeHelper.GetParent(current)
        return None
    # -----------------------------

    def _setup_data(self):
        self.categories = [
            "Structural Columns",
            "Structural Framing",
            "Structural Walls",
            "Floors",
            "Generic Models",
            "Stairs"
        ]
        
        # Load categories into XAML Resources so Template ComboBoxes can bind to them
        self._update_category_resources()
        
    def _update_category_resources(self):
        import System
        from System.Collections.Generic import List
        cat_list = List[System.String]()
        for c in self.categories:
            cat_list.Add(c)
        self.Resources["CategoryList"] = cat_list
        
        
        # Check if user has pre-selected elements
        from pyrevit import revit
        sel = revit.get_selection().element_ids
        if sel and len(sel) > 0:
            self.ScopeSelection.IsChecked = True
            self.ScopeView.IsChecked = False
        else:
            self.ScopeSelection.IsChecked = False
            self.ScopeView.IsChecked = True

    def _add_default_rules(self):
        rules_file = os.path.join(os.path.dirname(__file__), "rules.json")
        loaded = False
        if os.path.exists(rules_file):
            try:
                with open(rules_file, "r") as f:
                    saved_rules = json.load(f)
                if saved_rules and isinstance(saved_rules, list):
                    # Check if it's the old format (primary/secondary) or new format
                    if len(saved_rules) > 0 and "category" in saved_rules[0]:
                        for i, sd in enumerate(saved_rules):
                            self.rules.append({"category": sd["category"], "rank": i + 1})
                        loaded = True
            except:
                pass
                
        if not loaded:
            r1 = {"category": "Structural Columns", "rank": 1}
            r2 = {"category": "Structural Framing", "rank": 2}
            r3 = {"category": "Structural Walls", "rank": 3}
            r4 = {"category": "Floors", "rank": 4}
            self.rules.extend([r1, r2, r3, r4])
            
        self._refresh_ranks()

    def AddRule(self, sender, e):
        new_rule = {"category": "Generic Models", "rank": len(self.rules) + 1}
        self.rules.append(new_rule)
        self._refresh_ranks()

    def RemoveRule(self, sender, e):
        # Delete selected item, or delete last item if none selected
        curr = self.RuleList.SelectedItem
        if curr and curr in self.rules:
            self.rules.remove(curr)
            self._refresh_ranks()
        elif len(self.rules) > 0:
            self.rules.pop()
            self._refresh_ranks()

    def EditCategories(self, sender, e):
        # Explicit list of standard structural and architectural categories
        all_possible_cats = [
            "Columns", "Structural Columns", "Structural Framing",
            "Structural Walls", "Walls", "Floors", "Generic Models",
            "Roofs", "Stairs", "Ceilings", "Ramps", "Topography/Toposolid",
            "Structural Foundations", "Structural Connections", "Structural Rebar"
        ]
        all_possible_cats.sort()
        
        editor_xaml = os.path.join(os.path.dirname(__file__), "category_editor.xaml")
        editor = CategoryEditorWindow(editor_xaml, all_possible_cats, self.categories)
        editor.ShowDialog()
        
        if editor.confirmed:
            self.categories = [opt.Name for opt in editor.options if opt.IsSelected]
            self._update_category_resources()
            self.RuleList.Items.Refresh()

    def CloseWindow(self, sender, e):
        self.Close()

    def RunCommand(self, sender, e):
        try:
            use_selection = self.ScopeSelection.IsChecked
            is_unjoin = self.ModeUnjoin.IsChecked
            auto_linework = False
            
            self.Close()
            
            if use_selection:
                sel = revit.get_selection().element_ids
                if not sel:
                    forms.alert("선택된 요소가 없습니다. 화면에서 대상을 먼저 선택하거나 '현재 뷰' 범위를 사용하세요.", title="Auto Join")
                    return
            
            # Save rules for next time
            rules_file = os.path.join(os.path.dirname(__file__), "rules.json")
            try:
                with open(rules_file, "w") as f:
                    # Save as dictionaries matching the legacy structure
                    save_data = [{"category": r.get("category")} for r in self.rules]
                    json.dump(save_data, f)
            except:
                pass
            
            # Convert 1D priorities list into pairwise rules
            # e.g. [A, B, C] -> (A,A), (B,B), (C,C), (A,B), (A,C), (B,C)
            pairwise_rules = []
            for i in range(len(self.rules)):
                primary_cat = self.rules[i].get("category")
                if not primary_cat:
                    continue
                
                # 1. 동일 카테고리(자기 자신)끼리의 결합 규칙 추가 (보 vs 보 선 없애기 용도)
                pairwise_rules.append({
                    "primary": primary_cat,
                    "secondary": primary_cat,
                    "invert": False
                })
                
                # 2. 타 카테고리와의 위계 결합 규칙
                for j in range(i + 1, len(self.rules)):
                    secondary_cat = self.rules[j].get("category")
                    
                    if secondary_cat and primary_cat != secondary_cat:
                        pairwise_rules.append({
                            "primary": primary_cat,
                            "secondary": secondary_cat,
                            "invert": False
                        })
            
            with revit.Transaction("Heerim Auto Join"):
                results = autojoin_logic.execute_auto_join(
                    revit.doc, 
                    pairwise_rules, 
                    use_selection=use_selection, 
                    is_unjoin=is_unjoin,
                    auto_linework=auto_linework
                )
            
            output = script.get_output()
            output.close_others()
            output.print_md("### Auto Join Result")
            output.print_md("---")
            
            if is_unjoin:
                output.print_md("- Elements Unjoined: **{}**".format(results["unjoined"]))
                
                # Check for already unjoined elements (total - unjoined)
                matches = results.get("bb_matches", 0)
                already_unjoined = matches - results.get("unjoined", 0)
                if already_unjoined > 0:
                    output.print_md("- Already Unjoined (Skipped): **{}**".format(already_unjoined))
                    
                output.print_md("- Total Intersections Evaluated: **{}**".format(matches))
            else:
                output.print_md("- Elements Joined: **{}**".format(results["joined"]))
                
                # Check for already joined elements
                already_joined = results.get("already_joined", 0)
                if already_joined > 0:
                    output.print_md("- Already Joined (Skipped): **{}**".format(already_joined))
                    
                # Check for found intersections
                matches = results.get("bb_matches", 0)
                output.print_md("- Total Intersections Evaluated: **{}**".format(matches))
                if results.get("unjoinable", 0) > 0:
                    output.print_md("- Unjoinable elements (Skipped): **{}**".format(results["unjoinable"]))
                if results.get("switched", 0) > 0:
                    output.print_md("- Join Order Switched: **{}**".format(results["switched"]))
            
            if results["errors"] > 0:
                output.print_md("- Errors Encountered: **{}**".format(results["errors"]))
            
            if "error_msgs" in results and results["error_msgs"]:
                output.print_md("#### Error Details:")
                # 중복 에러 메시지 제거 및 출력 (최대 10개)
                unique_errors = list(set(results["error_msgs"]))
                for msg in unique_errors[:10]:
                    output.print_md("- `{}`".format(msg))
            
            if results["joined"] > 0 or results["unjoined"] > 0 or results.get("switched", 0) > 0:
                revit.uidoc.RefreshActiveView()
                
        except Exception as ex:
            import traceback
            forms.alert(traceback.format_exc(), title="Auto Join Error")

if __name__ == "__main__":
    xaml_file = os.path.join(os.path.dirname(__file__), "ui.xaml")
    window = AutoJoinWindow(xaml_file)
    window.ShowDialog()
