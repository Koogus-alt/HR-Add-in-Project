# -*- coding: utf-8 -*-
import os
import clr
from pyrevit import forms

import System
from System.Reflection import Assembly

# 경로 설정
current_dir = os.path.dirname(__file__)
dll_path = os.path.join(current_dir, "Heerim_SheetsAndView.dll")

if os.path.exists(dll_path):
    try:
        # 파일 잠금(Lock)을 방지하기 위해 파일의 바이트를 메모리로 통째로 읽어들여서 로드합니다. (핵심 비법)
        dll_bytes = System.IO.File.ReadAllBytes(dll_path)
        assembly = Assembly.Load(dll_bytes)
        
        # 타입 찾기
        target_type_name = "SheetsAndViewCommand"
        command_type = None
        
        for t in assembly.GetTypes():
            if t.Name == target_type_name or t.FullName.endswith("." + target_type_name):
                command_type = t
                break
        
        if command_type:
            # static void Run(UIApplication uiApp) 메서드를 실행합니다.
            run_method = command_type.GetMethod("Run")
            if run_method:
                # __revit__ 은 UIApplication 인스턴스입니다.
                args = System.Array[System.Object]([__revit__])
                run_method.Invoke(None, args)
            else:
                forms.alert("DLL 내에서 'Run' 메서드를 찾을 수 없습니다.")
        else:
            forms.alert("클래스를 찾을 수 없습니다: {}".format(target_type_name))
            
    except Exception as e:
        error_info = str(e)
        if hasattr(e, "InnerException") and e.InnerException:
            error_info += "\n\nInner Error: " + str(e.InnerException)
        
        import traceback
        forms.alert("DLL 실행 중 오류가 발생했습니다:\n{}\n\n{}".format(error_info, traceback.format_exc()))
else:
    forms.alert("Heerim_SheetsAndView.dll 빌드 파일을 찾을 수 없습니다. C# 빌드를 확인해주세요.")
