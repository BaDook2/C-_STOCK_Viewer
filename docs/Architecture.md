# 구조

- StockViewer.Core: 공통 시세 모델, 초성 검색, 공개 시세·공개 자동완성, 테스트용 데모. 기존 키움 서비스는 레거시 호환 코드로 보존하며 앱에서 호출하지 않습니다.
- StockViewer.App: WPF 검색·한 줄 가격 창, 코드별 창 수명 관리, 네이티브 위치 고정, 표시 기본값·목록 캐시.
- StockViewer.Tests: 실행형 회귀 테스트와 실제 키 없는 공개 경로 smoke. 네트워크 테스트는 --public-smoke로 명시적으로 구분합니다.

실제 위치는 C:\Users\wkdru\Desktop\C#\StockViewer입니다. 이전 채팅 폴더의 PROJECT_LOCATION.md를 따라 이 위치에서 작업합니다.

시작 → PublicMarketData → 캐시와 공개 기본 목록 → 입력 디바운스/취소 → 공개 자동완성 → 코드별 QuoteWindow → 공통 배치 폴링 → Dispatcher → 가격 갱신.

Quote는 공개 제공처·기준 시각·장 상태를 보존합니다. 종목별 다음 허용 조회 시각을 공유하고, 초기 조회와 정기 폴링의 중복을 막습니다. 기본 2초보다 긴 제공처 pollingInterval과 HTTP Retry-After를 우선합니다.

미국 종목의 내부 Code는 `US:`와 검색 응답의 reutersCode를 결합합니다. `US:AAPL.O`, `US:SPY`, `US:BRKb`처럼 저장하여 국내 6자리 코드와 충돌하지 않게 하고, 요청할 때만 접두사를 제거합니다. Stock.Symbol/DisplayCode는 화면의 티커와 정규화 검색에 사용합니다. 기존 국내 목록 캐시와 Quote 생성자의 기본 KRW 통화를 유지합니다. 레거시 키움 서비스는 국내 코드만 허용합니다.

PublicMarketData는 국내·미국을 별도 경로로 배치 조회하며 종목별 다음 조회 시각을 유지합니다. 미국 Quote는 USD 통화, 거래소, 최신 세션의 기준 시각을 보존합니다. 최신 장전·장후 가격은 세션별 전일 기준을 검증한 뒤 사용하며 전일 대비 등락률을 다시 계산합니다. 불완전한 연장 시세는 유효한 정규장 시세로 처리합니다. UI의 통화 표시는 Quote.Currency를 사용합니다.

팝업의 이름 표시 설정은 생성 시의 스냅샷입니다. 검색 창 체크박스의 후속 변경은 이미 존재하는 창에 전달하지 않습니다. 서비스 전환·재연결은 이름 표시 여부를 변경하지 않고 가격만 초기화합니다.

우클릭은 WPF MouseButtonEventArgs.ClickCount를 사용합니다. 첫 누름은 고정 토글, 두 번째 누름은 종료 예약입니다. 가격 창이 CaptureMode.SubTree로 우클릭의 Up까지 캡처하고 PreviewMouseRightButtonUp을 Handled 처리합니다. 종료는 DispatcherPriority.Background에 예약하여 현재 Up 메시지 처리가 끝난 뒤 캡처를 해제하고 창을 파괴합니다. WM_CONTEXTMENU는 가격 창의 HwndSourceHook에서 소비합니다. 전역 마우스 후크나 Windows 설정 변경은 사용하지 않습니다. 고정 위치는 WM_WINDOWPOSCHANGING에서 SWP_NOMOVE를 설정해 시스템 이동도 막습니다.

Windows의 [WM_RBUTTONUP 규칙](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-rbuttonup)에 따라 캡처 중인 창이 release를 받습니다. [WM_CONTEXTMENU 문서](https://learn.microsoft.com/en-us/windows/win32/menurc/wm-contextmenu)는 우클릭 Up 처리에서 메뉴 요청이 생성됨을 설명합니다.

검색 창의 Closed는 검색 작업과 자동완성만 정리합니다. 가격 창이 남아 있으면 공통 서비스·구독·시세 Dispatcher 처리는 계속 유지합니다. 검색 창이 닫힌 상태에서 마지막 가격 창이 닫히면 EndSessionAsync가 요청을 취소하고 서비스를 Dispose한 뒤 SessionEnded를 알립니다. App은 OnExplicitShutdown으로 실행하며 이 알림을 받은 뒤 종료합니다. 가격 창이 없는 검색 창을 닫는 경우에도 같은 종료 경로를 사용합니다.

X는 ClickMode.Press로 처리하고 창 이동용 Border는 버튼·체크박스의 형제 요소로 분리합니다. 자동완성 Popup은 StaysOpen=true로 마우스 캡처를 하지 않으며 포커스와 바깥 클릭으로 닫습니다. OpenSelectionAsync는 원격 검색 대기 전후에 searchClosed를 확인하여 닫은 검색 UI의 재개방 요청을 막습니다. 설정 버튼과 SettingsWindow는 제거했습니다.

자동완성은 GotKeyboardFocus에서 열지 않습니다. 입력·왼쪽 클릭·방향키가 RefreshSuggestions(openDropdown: true)를 요청하고, 카탈로그 갱신은 기본 인수로 현재 열림 상태만 유지합니다. 목록이 열리려면 검색 창이 활성 상태이고 검색칸에 키보드 포커스와 검색어가 있어야 합니다. 가격 창 종료 후 포커스 복귀는 검색 요청이나 목록 열기를 발생시키지 않습니다.

가격 창은 PopupPlacement.BelowSearch로 검색 창 아래에 가로 중앙 정렬하며 화면 하단에서는 위쪽으로 배치합니다. 초기 위치부터 입력칸을 덮지 않도록 Measure 뒤 좌표를 설정합니다.

디자인 기준은 사용자의 기존 STOCK/Stock.App/Styles/TossInspired.xaml입니다. 검색 창 286×80 DIP, 가격 창 높이 32 DIP를 유지하고 가격 팝업에는 별도 버튼과 메뉴를 표시하지 않습니다.
