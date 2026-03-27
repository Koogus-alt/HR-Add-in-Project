# -*- coding: utf-8 -*-
import os
import re
import clr
from pyrevit import forms

# Import System for Reflection
import System
from System.Reflection import Assembly

def get_latest_dll(folder, prefix):
    """폴더 내에서 특정 접두사로 시작하고 버전 번호가 있는 최신 DLL을 찾습니다."""
    if not os.path.exists(folder):
        return None
    dll_files = [f for f in os.listdir(folder) if f.startswith(prefix) and f.endswith(".dll")]
    if not dll_files:
        return None
    
    # 버전 번호 추출 및 정렬 (예: Heerim_AutoPlacement_v13.dll -> 13)
    def extract_version(filename):
        match = re.search(r'_v(\d+)', filename)
        return int(match.group(1)) if match else 0

    latest_file = max(dll_files, key=extract_version)
    return os.path.join(folder, latest_file)

# 경로 설정
current_dir = os.path.dirname(__file__)
dll_prefix = "Heerim_AutoPlacement"
latest_dll_path = get_latest_dll(current_dir, dll_prefix)

if latest_dll_path and os.path.exists(latest_dll_path):
    try:
        # Assembly 로드
        assembly = Assembly.LoadFrom(latest_dll_path)
        
        # 타입 찾기
        target_type_name = "AutoPlacementCommand"
        command_type = None
        
        for t in assembly.GetTypes():
            if t.Name == target_type_name or t.FullName.endswith("." + target_type_name):
                command_type = t
                break
        
        if command_type:
            # static void Run(UIApplication uiApp) 메서드를 실행합니다.
            run_method = command_type.GetMethod("Run")
            if run_method:
                # 실행 직전 알림
                # forms.alert("C# '{}' 실행을 시도합니다.".format(latest_dll_path), title="진행 중")
                
                # IronPython 리스트를 C# object[] 배열로 변환하여 전달합니다.
                args = System.Array[System.Object]([__revit__])
                run_method.Invoke(None, args)
            else:
                forms.alert("DLL 내에서 'Run' 메서드를 찾을 수 없습니다.")
        else:
            forms.alert("클래스를 찾을 수 없습니다: {}".format(target_type_name))
            
    except Exception as e:
        # Reflection 호출 시 실제 오류는 InnerException에 담겨 있는 경우가 많습니다.
        error_info = str(e)
        if hasattr(e, "InnerException") and e.InnerException:
            error_info += "\n\nInner Error: " + str(e.InnerException)
        
        import traceback
        forms.alert("DLL 실행 중 오류가 발생했습니다:\n{}\n\n{}".format(error_info, traceback.format_exc()))
else:
    forms.alert("최신 빌드(v*)를 찾을 수 없습니다. 빌드 후 다시 시도해주세요.")
