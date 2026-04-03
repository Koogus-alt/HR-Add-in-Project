# -*- coding: utf-8 -*-
import os
import stat
from pyrevit import revit, DB, UI, forms, script

# --- Settings ---
DEFAULT_LIBRARY = r"C:\Users\thomashj\Desktop\HR Add-in Project\C#\Heerim_SmartAssist\library"

def upgrade_families():
    # 1. Ask for Folder
    selected_path = forms.pick_folder(title="업그레이드할 라이브러리 폴더 선택")
    if not selected_path:
        return

    # 2. Confirm Action
    if not forms.alert("선택한 폴더의 모든 패밀리를 Revit 2026 버전으로 일괄 업그레이드하시겠습니까?\n\n경로: {}".format(selected_path),
                      yes=True, no=True):
        return

    # 3. Find All .rfa files (Recursive)
    rfa_files = []
    for root, dirs, files in os.walk(selected_path):
        for f in files:
            if f.lower().endswith(".rfa"):
                rfa_files.append(os.path.join(root, f))

    if not rfa_files:
        forms.alert("해당 폴더에 패밀리(.rfa) 파일이 존재하지 않습니다.")
        return

    output = script.get_output()
    output.print_md("### 🚀 라이브러리 일괄 업그레이드 시작 (총 {}개)".format(len(rfa_files)))
    
    # 4. Batch Process
    app = __revit__.Application
    count = 0
    errors = []

    with forms.ProgressBar(title="패밀리 업그레이드 중...", total=len(rfa_files)) as pb:
        for i, file_path in enumerate(rfa_files):
            file_name = os.path.basename(file_path)
            pb.update_progress(i + 1, len(rfa_files))
            pb.title = "[{}/{}] Upgrading: {}".format(i+1, len(rfa_files), file_name)

            try:
                # 0. Check and Remove Read-Only attribute
                if os.path.exists(file_path):
                    file_stats = os.stat(file_path)
                    if not (file_stats.st_mode & stat.S_IWRITE):
                        os.chmod(file_path, stat.S_IWRITE)
                        output.print_md("- 🔧 **읽기전용 해제됨**: {}".format(file_name))

                # Open Document
                open_opt = DB.OpenOptions()
                doc = app.OpenDocumentFile(DB.ModelPathUtils.ConvertUserVisiblePathToModelPath(file_path), open_opt)
                
                if doc:
                    # Save (Upgrades to Current Version automatically on save)
                    save_opt = DB.SaveAsOptions()
                    save_opt.OverwriteExistingFile = True
                    
                    doc.SaveAs(file_path, save_opt)
                    doc.Close(False)
                    
                    output.print_md("- ✅ **성공**: {}".format(file_name))
                    count += 1
                else:
                    errors.append("{} (열기 실패: 문서를 열 수 없습니다)".format(file_name))
                    output.print_md("- ❌ **실패(열기실패)**: {}".format(file_name))
            except Exception as e:
                err_msg = str(e).replace("\n", " ")
                errors.append("{} (에러: {})".format(file_name, err_msg))
                output.print_md("- ❌ **실패**: {} (사유: {})".format(file_name, err_msg))

    # 5. Result
    output.print_md("---")
    output.print_md("### 🏁 작업 완료!")
    output.print_md("- 성공: {} / {}".format(count, len(rfa_files)))
    
    if errors:
        output.print_md("#### ⚠️ 오류 목록")
        for err in errors:
            output.print_md("- " + err)

    forms.alert("업그레이드 작업이 완료되었습니다.\n성공: {} / {}".format(count, len(rfa_files)))

if __name__ == "__main__":
    upgrade_families()
