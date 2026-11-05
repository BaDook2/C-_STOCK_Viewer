# 구조

- StockViewer.Core: 공통 시세 모델, 초성 검색, 공개 시세·공개 자동완성, 테스트용 데모. 기존 키움 서비스는 레거시 호환 코드로 보존하며 앱에서 호출하지 않습니다.
- StockViewer.App: WPF 검색·한 줄 가격 창, 코드별 창 수명 관리, 네이티브 위치 고정, 표시 기본값·목록 캐시.
- StockViewer.Tests: 실행형 회귀 테스트와 실제 키 없는 공개 경로 smoke. 네트워크 테스트는 --public-smoke로 명시적으로 구분합니다.

실제 위치는 C:\Users\wkdru\Desktop\C#\StockViewer입니다. 이전 채팅 폴더의 PROJECT_LOCATION.md를 따라 이 위치에서 작업합니다.

시작 → PublicMarketData → 캐시와 공개 기본 목록 → 입력 디바운스/취소 → 공개 자동완성 → 코드별 QuoteWindow → 공통 배치 폴링 → Dispatcher → 가격 갱신.

Quote는 공개 제공처·기준 시각·실제 수신 시각·장 상태를 보존합니다. PublicMarketData의 MarketState를 국내·미국으로 분리하고 각각의 Gate·Wake·캐시·다음 조회 시각·오류·연속 실패 횟수를 관리합니다. 두 작업은 ThreadPool에서 독립 실행하며 초기 조회도 같은 시장의 게이트와 캐시를 공유합니다. 목표 간격은 1초이고 요청 시작 시점을 기준으로 다음 시각을 계산합니다. pollingInterval은 참고 메타데이터로만 보관하며 실제 HTTP Retry-After는 준수합니다.

시세 누락이나 실패 시 해당 종목의 NextFetch만 1·2·4·8·10초로 늦추며, 정상 종목은 계속 갱신합니다. ConnectionStatus.Code로 연결 상태를 종목에만 전달해 다른 가격 창이 흐려지지 않게 합니다. 캐시를 새 팝업에 반환할 때 마지막 연결 상태도 재전달합니다. HTTP 제한 시간은 5초이며 GetJsonAsync와 GetSnapshotAsync는 서비스 lifetime을 호출자 토큰에 연결합니다. 조회·JSON 처리는 ConfigureAwait(false)를 사용하고 UI 반영은 종료 상태를 확인하는 DispatchFromService를 거칩니다.

QuoteWindow는 마지막으로 확인한 체결 시각과 최근 HTTP 응답 수신 시각을 별도로 보존합니다. 공개 시세의 체결 시각이 잠시 빠져도 최신 수신 가격은 반영하며, 뒤늦게 온 오래된 체결 시각이나 캐시가 최신 가격을 덮지 않게 합니다. 레거시 KRX 틱과 스냅샷의 구분은 그 경로에만 적용합니다. 가격을 보존한 경우에도 툴팁의 수신 시각은 실제 응답에 맞춰 갱신합니다.

미국 종목의 내부 Code는 `US:`와 검색 응답의 reutersCode를 결합합니다. `US:AAPL.O`, `US:SPY`, `US:BRKb`처럼 저장하여 국내 6자리 코드와 충돌하지 않게 하고, 요청할 때만 접두사를 제거합니다. Stock.Symbol/DisplayCode는 화면의 티커와 정규화 검색에 사용합니다. 기존 국내 목록 캐시와 Quote 생성자의 기본 KRW 통화를 유지합니다. 레거시 키움 서비스는 국내 코드만 허용합니다.

PublicMarketData는 국내·미국을 별도 경로로 배치 조회하며 종목별 다음 조회 시각을 유지합니다. 미국 Quote는 USD 통화, 거래소, 최신 세션의 기준 시각을 보존합니다. 최신 장전·장후 가격은 세션별 전일 기준을 검증한 뒤 사용하며 전일 대비 등락률을 다시 계산합니다. 불완전한 연장 시세는 유효한 정규장 시세로 처리합니다. UI의 통화 표시는 Quote.Currency를 사용합니다.

팝업의 이름 표시 설정은 생성 시의 스냅샷입니다. 검색 창 체크박스의 후속 변경은 이미 존재하는 창에 전달하지 않습니다. 서비스 전환·재연결은 이름 표시 여부를 변경하지 않고 가격만 초기화합니다.

QuoteWindow의 PreviewMouseDown/Up은 좌우 버튼 상태를 관리하고 CaptureMode.SubTree로 각 Up까지 캡처합니다. 단일 우클릭은 Up에서 Fix를 전환하고 더블클릭은 ClickCount로 판정해 두 번째 Up 이후 DispatcherPriority.Background에서 종료합니다. 좌우 동시 누름은 고정·종료 예약을 취소하고, 마지막 Up 이후 SearchRequested를 한 번 발생시킵니다. 동시 클릭 뒤의 첫 우클릭에는 이전 ClickCount를 적용하지 않습니다. 캡처가 외부로 넘어가면 미완성 제스처를 취소합니다. WM_CONTEXTMENU는 가격 창의 HwndSourceHook에서 소비합니다. 전역 마우스 후크나 Windows 설정 변경은 사용하지 않습니다.

왼쪽 드래그는 화면 픽셀 이동량의 DIP 변환으로 최소 드래그 거리를 확인한 뒤 소유한 가격 HWND의 SetWindowPos에 반영합니다. 픽셀 기준 시작 위치를 유지해 모니터 DPI가 바뀌어도 이동량이 갑자기 다른 비율로 적용되지 않게 합니다. 모달 DragMove 루프를 사용하지 않아 이동 중에도 오른쪽 버튼의 누름을 받을 수 있습니다. 고정 상태는 드래그를 반영하지 않고 WM_WINDOWPOSCHANGING의 SWP_NOMOVE로 시스템 이동도 막습니다.

Windows의 [WM_RBUTTONUP 규칙](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-rbuttonup)에 따라 캡처 중인 창이 release를 받습니다. [WM_CONTEXTMENU 문서](https://learn.microsoft.com/en-us/windows/win32/menurc/wm-contextmenu)는 우클릭 Up 처리에서 메뉴 요청이 생성됨을 설명합니다.

검색 창의 Closing은 가격 창이 있을 때 Cancel=true와 Hide로 검색 UI만 숨깁니다. 검색 작업과 자동완성을 취소하고 공통 서비스·구독·시세 처리는 유지합니다. SearchRequested는 같은 검색 Window를 Show/복원해 이동시키므로 검색어·체크박스·시세 상태가 유지됩니다. Loaded 초기 연결은 한 번만 실행합니다. WPF의 [창 숨김과 Closing 취소 지침](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/windows/how-to-close-window-dialog-box#cancel-close-and-hide)을 따릅니다.

숨긴 검색 창에서 마지막 가격 창이 닫히면 EndSessionAsync가 요청을 취소하고 서비스를 Dispose한 뒤 보존한 검색 창도 실제 Close하고 SessionEnded를 알립니다. App은 OnExplicitShutdown으로 실행하며 이 알림을 받은 뒤 종료합니다. 검색 창이 보이면 마지막 가격 창을 닫아도 검색은 계속할 수 있습니다. 가격 창이 없는 검색 창의 X는 종료합니다.

X는 ClickMode.Press로 처리하고 창 이동용 Border는 버튼·체크박스의 형제 요소로 분리합니다. 자동완성 Popup은 StaysOpen=true로 마우스 캡처를 하지 않으며 포커스와 바깥 클릭으로 닫습니다. OpenSelectionAsync는 원격 검색 대기 전후에 searchHidden과 searchVisibilityVersion을 확인합니다. 숨긴 뒤 재표시한 검색 창에서도 이전 검색 제출이 목록이나 가격 창을 열지 않도록 합니다. 설정 버튼과 SettingsWindow는 제거했습니다.

자동완성은 GotKeyboardFocus에서 열지 않습니다. 입력·왼쪽 클릭·방향키가 RefreshSuggestions(openDropdown: true)를 요청하고, 카탈로그 갱신은 기본 인수로 현재 열림 상태만 유지합니다. 목록이 열리려면 검색 창이 활성 상태이고 검색칸에 키보드 포커스와 검색어가 있어야 합니다. 가격 창 종료 후 포커스 복귀는 검색 요청이나 목록 열기를 발생시키지 않습니다.

가격 창은 PopupPlacement.BelowSearch로 검색 창 아래에 가로 중앙 정렬하며 화면 하단에서는 위쪽으로 배치합니다. 초기 위치부터 입력칸을 덮지 않도록 Measure 뒤 좌표를 설정합니다.

검색 창 재표시는 PopupPlacement.BelowQuote로 클릭한 가격 창 아래 4 DIP에 가로 중앙 정렬합니다. 같은 배치 함수를 공유하며 기준 가격 창이 있는 모니터의 작업 영역 아래 공간이 부족하면 위에 표시하고 좌우 가장자리도 제한합니다.

디자인 기준은 사용자의 기존 STOCK/Stock.App/Styles/TossInspired.xaml입니다. 검색 창 286×80 DIP, 가격 창 높이 32 DIP를 유지하고 가격 팝업에는 별도 버튼과 메뉴를 표시하지 않습니다.
