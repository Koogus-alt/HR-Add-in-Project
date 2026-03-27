# 원격 데스크탑(RDP) 배포 가이드

원격지의 다른 컴퓨터에서 `Heerim.extension`을 동일하게 사용하기 위한 방법입니다.

## 1. 준비물
*   현재 컴퓨터에서 생성된 `Heerim_Extension_Deploy.zip` 파일
*   원격지 컴퓨터에 설치된 **Revit 2026**
*   **pyRevit** (미설치 시 아래 단계에서 설치)

## 2. 배포 단계

### 단계 0: pyRevit 설치 (원격 컴퓨터)
Heerim 도구는 pyRevit 기반으로 작동하므로, 원격 컴퓨터에 pyRevit이 없다면 먼저 설치해야 합니다.
1.  **다운로드**: [pyRevit 공식 GitHub Releases](https://github.com/eirannejad/pyRevit/releases)에 접속합니다.
2.  **파일 선택**: 최신 버전(예: v4.8.x)의 `pyRevit_Setup_vX.X.X.exe` 파일을 다운로드합니다.
3.  **설치**: 다운로드한 파일을 실행하여 설치를 완료합니다. (설치 중 옵션은 기본값으로 두시면 됩니다.)

### 단계 1: 파일 전달
1.  현재 컴퓨터에 생성된 `Heerim_Extension_Deploy.zip` 파일을 복사합니다.
2.  원격 데스크탑 세션 내에서 원격지 컴퓨터의 원하는 폴더(예: `C:\Heerim_Tools`)에 붙여넣기합니다.
3.  해당 폴더에서 압축을 풉니다. (`Heerim.extension` 폴더가 보여야 합니다.)

### 단계 B: pyRevit 등록
1.  원격지 컴퓨터에서 **명령 프롬프트(CMD)** 또는 **PowerShell**을 관리자 권한으로 실행합니다.
2.  다음 명령어를 입력하여 pyRevit이 해당 확장을 인식하도록 합니다:
    ```bash
    pyrevit extend ui Heerim "압축을_푼_Heerim.extension_폴더의_전체_경로"
    ```
    *예: `pyrevit extend ui Heerim "C:\Heerim_Tools\Heerim.extension"`*

### 단계 C: Revit 실행
1.  원격지에서 Revit을 실행합니다.
2.  상단 탭에 **Heerim** 메뉴가 나타나는지 확인합니다.

---

## 💡 유의사항
*   **1603 오류 발생 시**: 만약 원격지에서 Revit 설치 자체가 막힌다면, 어제 안내해 드린 `RemoveODIS.exe` 실행 및 Temp 폴더 정리 단계를 먼저 진행해 주세요.
*   **경로 관리**: 가능하면 원격지에서도 영문으로 된 단순한 경로(예: `C:\Heerim`)를 사용하시는 것이 가장 안정적입니다.
