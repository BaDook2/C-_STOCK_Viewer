# StockViewer 구조

v2.0.0부터 UI는 Avalonia이며 WPF 프로젝트를 대체합니다. 실제 위치는 `C:\Users\wkdru\Desktop\C#\StockViewer`입니다.

- StockViewer.Core: 종목 모델·초성/티커 검색·네이버 공개 시세·시장별 독립 폴링. 기존 코어의 의미와 정밀도를 유지합니다. 키움 서비스는 레거시 소스로만 남아 있고 앱에서 호출하지 않습니다.
- StockViewer.App: Avalonia 검색 창·가격 팝업·사용자 테마·설정·창 수명. AssemblyName과 실행 파일은 StockViewer입니다.
- StockViewer.Tests: 시세 회귀 검증, Avalonia headless/Skia UI 렌더링과 raw 입력, 별도 Windows 네이티브 창 검증.

검색 창 286×80 DIP, 가격 창 높이 32 DIP와 기존 팔레트·글꼴·여백·모서리를 사용합니다. 입력칸·버튼·체크박스·목록 행을 사용자 템플릿으로 구현하며 한글 입력은 Avalonia TextBox/TextPresenter 경로를 유지합니다.

검색은 TextProperty 변경에서 동기적으로 시작하고 원격 요청은 250ms 디바운스·취소를 적용합니다. 검색어·세션·가시성 버전이 다른 응답은 반영하지 않습니다. 포커스 복귀나 검색 창 복원 자체는 자동완성을 펼치지 않습니다.

Avalonia는 추가 버튼 누름과 일부 버튼 해제를 PointerMoved로 전달하므로 전체 좌우 버튼 상태를 검사합니다. 동시 클릭은 고정·종료보다 우선하고 마지막 Up 이후 Dispatcher.Background에서 검색 요청을 한 번 보냅니다. Windows에서는 가격 HWND별 WndProc 훅으로 WM_CONTEXTMENU를 소비하고 위치 고정과 32 DIP 높이를 유지합니다.

Position은 화면 픽셀, 크기는 DIP입니다. 모니터 배율로 변환한 뒤 작업 영역을 기준으로 아래/위 배치와 가장자리 제한을 계산합니다. 검색 X는 가격 창이 있을 때 숨기고, 마지막 가격 창이 닫히면 서비스 정리 후 종료합니다.

설정은 `%LOCALAPPDATA%\StockViewer`를 사용합니다. 별도 시험판의 `%LOCALAPPDATA%\StockViewer_avalonia`에 있는 display-name.txt·stocks.json은 새 경로에 해당 파일이 없을 때만 이전합니다. 기존 설정을 덮거나 키 파일을 읽지 않습니다.

Core의 WPF 시절 기준 소스와 체크섬은 [이전 코어 기록](core-source-manifest.json)에 남기고, 그 시점의 WPF 파일 보존 확인은 docs/history에 기록합니다. 현재 빌드는 외부의 WPF 폴더를 참조하지 않습니다.

가격창 배치는 popup-layout.json에 버전·종목·물리 픽셀 좌표·고정 상태·종목명 표시 여부를 원자적으로 저장합니다. 불러오기 시 JSON과 종목 코드를 검증하며, 새로운 창은 첫 유효 시세 수신 후 표시합니다. 기존 창은 재사용하고 전체 복원 성공 시에만 저장 목록 밖의 창을 닫습니다. 모니터 작업 영역 밖의 위치는 현재 화면에 맞춰 제한합니다.

PopupTopmost는 모든 고정 창의 실제 HWND WS_EX_TOPMOST 또는 NSWindow level을 확인합니다. 활성화 변경과 1초 간격 점검에서 누락된 창만 비활성 상태로 복구하므로 정상 창을 반복해서 앞으로 올리지 않습니다. Windows의 고정 창 WndProc도 z-order 강등을 차단합니다. 창이 닫히거나 고정 해제되면 등록을 해제하고 마지막 고정 창이 사라지면 타이머를 중지합니다. 다른 창에서 넘어온 우클릭 횟수로 첫 우클릭을 더블클릭 닫기로 오인하지 않도록 창별 클릭 완료 상태를 확인합니다.
