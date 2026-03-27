# -*- coding: utf-8 -*-
from pyrevit import revit, DB, forms, script
import os
import sys

class StairGapWindow(forms.WPFWindow):
    def __init__(self, xaml_file_name):
        forms.WPFWindow.__init__(self, xaml_file_name)

    def Window_MouseDown(self, sender, e):
        import System
        if e.ChangedButton == System.Windows.Input.MouseButton.Left:
            self.DragMove()

    def Window_KeyDown(self, sender, e):
        import System
        if e.Key == System.Windows.Input.Key.Escape:
            self.Close()
        elif e.Key == System.Windows.Input.Key.Enter:
            self.RunCommand(sender, e)

    def CloseWindow(self, sender, e):
        self.Close()

    def RunCommand(self, sender, e):
        # Read the mode selections from XAML RadioButtons
        is_solid = self.ModeSolid.IsChecked
        
        self.Close()
        
        script_dir = os.path.dirname(__file__)
        if is_solid:
            target_script = os.path.join(script_dir, "solid_script.py")
        else:
            target_script = os.path.join(script_dir, "void_script.py")
            
        if os.path.exists(target_script):
            try:
                # Execute the legacy script within the current pyRevit scope
                with open(target_script, 'r') as f:
                    code_content = f.read()
                    
                compiled_code = compile(code_content, target_script, 'exec')
                exec(compiled_code, globals())
            except Exception as ex:
                import traceback
                forms.alert("스크립트 실행 중 오류가 발생했습니다:\n{}".format(ex), title="Execute Error")
        else:
            forms.alert("대상 스크립트를 찾을 수 없습니다: " + target_script)

if __name__ == "__main__":
    xaml_file = os.path.join(os.path.dirname(__file__), "ui.xaml")
    window = StairGapWindow(xaml_file)
    window.ShowDialog()
