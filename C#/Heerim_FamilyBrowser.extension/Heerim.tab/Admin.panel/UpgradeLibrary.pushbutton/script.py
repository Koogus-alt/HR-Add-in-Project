# -*- coding: utf-8 -*-
from pyrevit import revit, DB, UI, script
import os

# 1. 대상 라이브러리 경로 설정
lib_path = r'C:\Users\thomashj\Desktop\기존 개발 프로그램\Heerim BIM Library Browser\서버라이브러리'
output = script.get_output()

def upgrade_families():
    if not os.path.exists(lib_path):
        print("라이브러리 경로를 찾을 수 없습니다: {}".format(lib_path))
        return

    # 2. 모든 .rfa 파일 수집
    family_files = []
    for root, dirs, files in os.walk(lib_path):
        for file in files:
            if file.lower().endswith(".rfa"):
                family_files.append(os.path.join(root, file))

    total = len(family_files)
    print("총 {}개의 패밀리를 발견했습니다. 업그레이드를 시작합니다...".format(total))
    
    app = revit.doc.Application
    count = 0
    success_count = 0
    fail_count = 0

    # 3. 루프 실행 (하나씩 열고 저장하고 닫기)
    for f_path in family_files:
        count += 1
        file_name = os.path.basename(f_path)
        
        try:
            print("[{}/{}] 작업 중: {}".format(count, total, file_name))
            
            # 패밀리 파일 열기
            # OpenOptions를 사용하여 업그레이드 경고 무시 시도
            options = DB.OpenOptions()
            f_doc = app.OpenDocumentFile(DB.ModelPathUtils.ConvertUserVisiblePathToModelPath(f_path), options)
            
            if f_doc.IsFamilyDocument:
                # 저장 (이 과정에서 현재 레빗 버전으로 업그레이드됨)
                f_doc.Save()
                f_doc.Close(True)
                success_count += 1
            else:
                f_doc.Close(False)
                fail_count += 1
                
        except Exception as e:
            print("  >> !!! 에러 발생 ({}): {}".format(file_name, str(e)))
            fail_count += 1

    print("\n--- 작업 완료 ---")
    print("성공: {} / 실패: {}".format(success_count, fail_count))

if __name__ == "__main__":
    # 사용자 확인 창
    res = UI.TaskDialog.Show("Library Batch Upgrade", 
                            "라이브러리 전체를 Revit 2026 버전으로 업그레이드하시겠습니까?\n이 작업은 시간이 오래 걸릴 수 있습니다.", 
                            UI.TaskDialogCommonButtons.Yes | UI.TaskDialogCommonButtons.No)
    
    if res == UI.TaskDialogResult.Yes:
        upgrade_families()
