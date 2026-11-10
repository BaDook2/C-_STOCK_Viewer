# Avalonia 이식 검증 · 2026-11-08

- 원본: `C:\Users\wkdru\Desktop\C#\StockViewer`, 커밋 e8379d1 (v1.1.2 검증 기록). 대상: `C:\Users\wkdru\Desktop\C#\StockViewer_avalonia`. 별도 소스·설정 저장 경로를 사용하고 원본 프로젝트 참조는 없음.
- .NET 10 / Avalonia 12.1.5 Release 빌드 경고 0, 오류 0. UI에 UseWPF나 System.Windows 참조가 없음을 확인함.
- 원본 추적 파일과 루트·publish WPF 실행 파일을 포함한 30개 파일의 SHA256이 작업 전 기록과 동일함. 새 Core 소스도 원본과 파일별 체크섬이 동일함. 기록은 docs/core-source-manifest.json과 docs/original-preservation-manifest.json에 보관함.

## 기능

- 전체 헤드리스 회귀 검증 **226개 통과**. 원본의 UI 독립 검색·시세·캐시·1초 갱신·시장별 독립 처리·종목별 오류 복구·Retry-After·미국 장전/장후 계산 검증을 재사용하고 Avalonia 창·검색·입력 검증을 추가함.
- 별도 실제 Windows UI 검증 **111개 통과**. 실제 HWND와 투명 배경, 286×80 검색 창, 높이 32 가격 창, Fix의 Topmost와 네이티브 이동 금지, WM_CONTEXTMENU 소비, X 숨김, 같은 검색 HWND 재표시·최소화 복원과 마지막 팝업 종료를 확인함.
- raw 포인터 검증에서 Avalonia의 두 번째 버튼 누름/부분 해제가 PointerMoved로 전달되는 것을 확인하고 전체 버튼 상태로 처리함. 모든 좌우 누름/해제 순서와 Fix/더블클릭 우선순위, 마지막 Up 이후 검색 요청·포인터 캡처 해제를 통과함.
- 원본의 새 팝업별 이름 표시, 동일 종목 교체, 한 줄 원화/달러 표시, 초성/티커 검색, 한글 두 글자 초과 입력, 반복 삭제/재입력, 실제 자동완성 행 클릭, 포커스 복귀 시 목록 닫힘을 검증함.
- 가격 팝업의 좌우 동시 클릭으로 검색 창을 재사용하고 아래/위 배치를 확인함. 화면 작업 영역과 150% 배율 계산, 음수 모니터 좌표도 검증함. 실제 서로 다른 DPI의 다중 모니터 이동은 이번 환경에서 별도 실측하지 않음.
- 초기 시세 대기 중 입력 유지, X 후 갱신 유지, 숨긴 창의 마지막 가격 팝업 종료 시 서비스 정리를 검증함. 설정은 `%LOCALAPPDATA%\StockViewer_avalonia`로 분리함.

## 디자인

- 원본 XAML의 팔레트·글꼴·모서리·여백·입력칸·버튼·체크박스·목록 행 템플릿을 Avalonia AXAML로 구현함. 원본의 자동 TextBlock 글자 크기를 명시적으로 맞춤.
- 실제 Windows의 기본 최소 창 높이가 39픽셀로 적용되는 차이를 창별 WM_WINDOWPOSCHANGING 처리로 보정하여 가격 팝업 높이를 정확히 32 DIP로 확인함. 전역 Windows 설정이나 다른 창은 변경하지 않음.
- artifacts/ui/search-empty.png, quote-named.png, quote-price-only.png, quote-us.png, search-dropdown.png를 Skia로 렌더링해 원본 이미지와 비교함. 텍스트 렌더링 엔진이 달라 글자 가장자리 안티앨리어싱까지 비트 단위로 동일하다고 보장하지 않음.

## 실제 시세

- `--public-smoke` **10개 검증 통과**. 국내 목록·삼성전자 검색/시세, AAPL/애플의 같은 종목 식별자, AAPL·SPY·BRK B 달러 시세와 두 시장의 연속 조회 각 3개를 확인함.
- 평균 수신 간격: 삼성전자 **1.011초**, 애플 **1.013초**. 확인 시점은 일요일 장 마감 상태여서 실제 체결 가격의 매초 변화는 검증하지 않음. API 키·증권사 인증·주문 요청은 사용하지 않음.
- Windows 실행을 검증한 결과이며 macOS/Linux 실제 실행 검증으로 간주하지 않음.

## 실행 파일

- Windows x64 self-contained 단일 파일 게시 완료. 프로젝트 루트와 publish/StockViewer_avalonia.exe가 동일한 배포 파일임을 확인함. publish에는 EXE 하나만 남기며 개발용 네이티브 PDB는 배포하지 않음.
- 다른 파일이 없는 폴더에 EXE만 복사해 5초 이상 정상 실행 상태를 유지함을 확인함. .NET 및 Skia/HarfBuzz DLL을 옆에 설치할 필요가 없음.
- 제품 버전 1.1.2, 크기 **48,674,904 bytes**.
- SHA256: **4A989ABFA1CCF5CAF4A7546EB0FB433CC1731FED7332E8AE2290E723C135B9BF**.
- 원본 GitHub 저장소·태그·Release는 변경하지 않음. 새 프로젝트에 향후 사용할 독립 build.yml 구성만 포함함.

# StockViewer 통합과 v2.0.0 · 2026-11-08

- 사용자 요청으로 기존 WPF 작업 폴더를 제거하고 Avalonia 프로젝트를 StockViewer로 전환함. 위 기록은 최초 독립 이식 당시의 보존 기록이며, v2.0.0부터 실제 프로젝트·실행 파일·설정 기본 경로는 StockViewer를 사용함.
- AssemblyName·Avalonia 리소스 URI·manifest·창 제목·솔루션·Publish.ps1·Release workflow의 파일명을 StockViewer로 통일함. UI는 net10.0 Avalonia이고 UseWPF·System.Windows 참조는 사용하지 않음.
- 기존 Git 저장소 이력과 remote를 유지하고 이전 WPF 버전의 태그·Release는 남김. 새 Release는 Avalonia StockViewer.exe를 제공하도록 workflow를 교체함.
- README·구조·배포·시세 문서를 현재 프로그램 기준으로 정리함. 별도 시험판 설정 폴더의 표시 설정과 검색 캐시는 새 경로에 파일이 없을 때만 이전하며 기존 설정·원본 키 파일은 변경하지 않음.
- 설정 이전 회귀 검증 5개 추가: 표시 설정·검색 캐시 이전, 키 파일 미복사, 기존 설정 미덮어쓰기, 이전 사용자 파일 유지.
- 이름 변경 후 Release 빌드 경고 0/오류 0, 헤드리스 231개·Windows 네이티브 116개·실제 공개 시세 10개 검증 통과. 실제 연속 수신 간격은 삼성전자 1.003초, 애플 1.008초였으며 확인 시점은 장 마감 상태임.
- Git 이력과 origin을 확인해 .git을 보존한 뒤 기존 WPF 폴더를 삭제하고 StockViewer_avalonia를 StockViewer로 이동 완료함. 이전 폴더와 이전 이름의 실행 파일은 남아 있지 않으며 현재 소스에 WPF 프로젝트 참조가 없음을 확인함.
- v2.0.0 Release 게시 완료: https://github.com/BaDook2/C-_STOCK_Viewer/releases/tag/v2.0.0
- Actions 성공: https://github.com/BaDook2/C-_STOCK_Viewer/actions/runs/42434406454. Avalonia Release 빌드·헤드리스 회귀·Windows 창 검증·단일 EXE 시작 확인·Release 첨부를 통과함.
- GitHub 저장소 소개를 현재 Avalonia 프로그램 설명으로 갱신했고 README와 릴리즈 본문도 같은 기능을 설명하도록 정리함. 최신 Release가 v2.0.0임을 별도 조회로 확인함.
- 실제 첨부 StockViewer.exe의 GitHub digest·크기·버전 일치를 확인하고 파일 하나만 있는 폴더에서 5초 시작 확인을 통과함. 프로젝트 루트·publish 실행 파일은 실제 Release 파일로 교체하고 체크섬 일치를 확인함.
- 크기 48,688,056 bytes, 제품 버전 2.0.0+0b33da980cbbdfc112e3ec85918b1d12b8d9a30d.
- SHA256: 01A9F409E21BD184DBBB6EDD0B3314C24E91EC69A2ACE2BA1675193D0A391900.


# StockViewer v2.2.0 · 2026-11-10

- 실제 Mac 앱을 직접 실행해 공개 시세 가격창 20개를 검색·표시하고 각각 우클릭으로 고정함. 저장 버튼으로 20개를 저장한 후 앱을 종료·재실행하고 불러오기 버튼으로 20개의 위치·고정 상태가 복원됐다는 화면을 확인함. 저장 JSON도 팝업 20개·고정 20개임을 확인함.
- 내부 IsFixed 값뿐 아니라 네이티브 HWND의 WS_EX_TOPMOST / NSWindow level을 창마다 확인·보정하도록 수정함. Windows 고정 창의 z-order 강등을 WndProc에서 방지하며, Mac에서는 20개 실제 창의 level을 강제로 낮춘 뒤 모두 복구됨을 확인함. 한 창만 고정을 해제하면 나머지 19개는 그대로 유지함.
- 첫 우클릭에 다른 창에서 넘어온 click count가 2여도 고정으로 처리함. 같은 창의 두 번째 우클릭만 종료로 처리하며 기존 동시 클릭 우선순위를 유지함.
- 같은 종목을 재검색할 때는 사용자 요청대로 고정을 해제하고 검색창 근처의 빈자리에 다시 배치함. 배치 불러오기에만 저장된 좌표·고정 상태를 적용함.
- 종목·물리 좌표·고정·종목명 표시 여부를 버전이 있는 popup-layout.json에 원자적으로 저장함. 앱 재실행 후 10개 창의 정확한 round-trip, 중복 없는 재불러오기, 손상·버전 불일치·중복 종목 JSON의 기존 창 보존, 3초 미수신 시 부분 복원과 기존 창·저장 파일 보존을 확인함. 화면 밖 좌표를 작업 영역으로 제한하고, 복원된 창에 검색 컨트롤이 가려지면 검색창만 빈자리로 옮김.
- 수신 오류 때 가격창 전체를 55% 불투명도로 표시하던 처리를 제거함. 20개 가격창에 정상·끊김·조회 실패 상태를 혼합해도 라이트·다크 모드 배경이 완전히 불투명하고 마지막 시세를 유지함을 확인함. 장 마감 회색 표시·재개 시 등락 색상 복원 검사를 유지함.
- 저장·불러오기 아이콘을 테마 버튼 왼쪽에 추가하고 286×80 검색창에서 컨트롤이 겹치지 않도록 배치함. 두 새 버튼도 hover 시 글자·콘텐츠 배경을 바꾸지 않는 검사에 포함함.
- 로컬 Release 빌드 경고 0/오류 0. 전체 헤드리스 회귀 380개 통과 후 검색창 겹침 방지 검사를 추가함. 최종 헤드리스 UI·입력 검사 253개 및 Mac 실제 창 검사 190개 통과. Windows·Mac 각 아키텍처의 추가 검증은 릴리즈 workflow가 수행함.
