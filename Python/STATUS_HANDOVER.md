# Heerim Extension - Overall Project Status & Handover
*Documented on: 2026-02-25*

이 문서는 새로운 대화창에서 AI 어시스턴트가 `Heerim.extension` 프로젝트 상태를 즉시 파악하고, 다음 개발을 부드럽게 이어나갈 수 있도록 하기 위한 상태 요약본입니다.

## 1. 프로젝트 위치
- **Root**: `C:\Users\thomashj\AppData\Roaming\pyRevit\Extensions\Heerim.extension`
- **주요 모듈 경로**: `lib/autojoin_logic.py`, `Heerim.tab/AutoJoin.panel`, `Heerim.tab/Test.panel`

## 2. 완료된 핵심 도구 (Status)
- **Auto Join (Execute.pushbutton)**:
    - `규칙.json`(rules) 기반 우선순위 결합 및 결합 순서 전환 완벽 지원.
    - 선택 범위(현재 뷰 vs 유저 선택) 및 모드(Join vs Unjoin) 설정 가능한 전용 UI(`ui.xaml`) 구축 완료.
    - 출력창 로깅 (해당 없음, 이미 결합됨, 오류 등 상세 리포트).
- **Mockup (Test.panel)**:
    - **Default Mode (`01_Mockup`)**: 6층짜리 다각형 복합 건축물(Twisted Tower) 자동 스크립트 기반 생성 기능 구축.
    - **Save Mode (`02_Save`)**: 구조물(기둥, 보, 바닥, 베이직 벽, 지형 솔리드)의 좌표/곡선 데이터를 완벽히 추출해 `mockup_config.json` 백업 생성 기능 구축.
    - **Clone Mode (`01_Mockup`)**: 백업된 JSON 데이터를 기반으로 벽체와 보 등의 형상을 100% 동일하게 복원하는 클론 도화지 모드 완성.
    - **Delete Mode (`03_Delete`)**: 테스트용 Mockup 형상 및 JSON 설계도를 깔끔하게 제거하는 초기화 기능 완성.

## 3. 남은 과제 및 다음 스텝
- 기존 AutoTrim 툴의 고도화 (곡선 벽체, 전역 정리 등)
- Stair Gap 적응형 패밀리의 치수 기반 수정(Generic Model) 전환 연구 필요 여부
- **[새로운 도구 개발 대기 중]**: 사용자의 새로운 명령에 따라 신규 패널 또는 푸시버튼 개발 착수 예정 (예: 자동 치수 기입, 뷰 배치 자동화 등).

## 4. 새 대화창 시작 시 가이드
새 대화창을 열고 아래 문구를 복사해서 붙여넣으세요:
> "Heerim.extension 프로젝트 작업을 이어갈 거야. 루트 경로는 `C:\Users\thomashj\AppData\Roaming\pyRevit\Extensions\Heerim.extension`이야. 해당 루트에 있는 `STATUS_HANDOVER.md` 파일을 읽고 현재 구축된 AutoJoin과 Mockup 테스트 툴의 개발 완료 상태를 파악해 둬. 오늘은 기존 기능은 두고, 완전히 새로운 기능을 개발할 거야. [여기에 개발하고 싶은 새로운 내용을 적어주세요]"
