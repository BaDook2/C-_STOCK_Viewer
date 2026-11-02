# 구조

- StockViewer.Core: 공통 시세 모델, 초성 검색, 공개 시세·공개 자동완성, 테스트용 데모. 기존 키움 서비스는 레거시 호환 코드로 보존하며 앱에서 호출하지 않습니다.
- StockViewer.App: WPF 검색·설정·한 줄 가격 창, 코드별 창 수명 관리, 네이티브 위치 고정, 표시 기본값·목록 캐시.
- StockViewer.Tests: 실행형 회귀 테스트와 실제 키 없는 공개 경로 smoke. 네트워크 테스트는 --public-smoke로 명시적으로 구분합니다.

실제 위치는 C:\Users\wkdru\Desktop\C#\StockViewer입니다. 이전 채팅 폴더의 PROJECT_LOCATION.md를 따라 이 위치에서 작업합니다.

시작 → PublicMarketData → 캐시와 공개 기본 목록 → 입력 디바운스/취소 → 공개 자동완성 → 코드별 QuoteWindow → 공통 배치 폴링 → Dispatcher → 가격 갱신.

Quote는 공개 제공처·기준 시각·장 상태를 보존합니다. 종목별 다음 허용 조회 시각을 공유하고, 초기 조회와 정기 폴링의 중복을 막습니다. 기본 2초보다 긴 제공처 pollingInterval과 HTTP Retry-After를 우선합니다.

팝업의 이름 표시 설정은 생성 시의 스냅샷입니다. 검색 창 체크박스의 후속 변경은 이미 존재하는 창에 전달하지 않습니다. 서비스 전환·재연결은 이름 표시 여부를 변경하지 않고 가격만 초기화합니다.

우클릭은 WPF MouseButtonEventArgs.ClickCount를 사용합니다. 첫 클릭은 고정 토글, 두 번째 클릭은 닫기입니다. 고정 위치는 WM_WINDOWPOSCHANGING에서 SWP_NOMOVE를 설정해 시스템 이동도 막습니다.

검색 창의 Closed는 검색 작업과 자동완성만 정리합니다. 가격 창이 남아 있으면 공통 서비스·구독·시세 Dispatcher 처리는 계속 유지합니다. 검색 창이 닫힌 상태에서 마지막 가격 창이 닫히면 EndSessionAsync가 요청을 취소하고 서비스를 Dispose한 뒤 SessionEnded를 알립니다. App은 OnExplicitShutdown으로 실행하며 이 알림을 받은 뒤 종료합니다. 가격 창이 없는 검색 창을 닫는 경우에도 같은 종료 경로를 사용합니다.

가격 창은 PopupPlacement.BelowSearch로 검색 창 아래에 가로 중앙 정렬하며 화면 하단에서는 위쪽으로 배치합니다. 초기 위치부터 입력칸을 덮지 않도록 Measure 뒤 좌표를 설정합니다.

디자인 기준은 사용자의 기존 STOCK/Stock.App/Styles/TossInspired.xaml입니다. 검색 창 286×80 DIP, 가격 창 높이 32 DIP를 유지하고 가격 팝업에는 별도 버튼과 메뉴를 표시하지 않습니다.
