# 검증 결과 · 2026-10-31

- 실제 실행 파일: C:\Users\wkdru\Desktop\C#\StockViewer\StockViewer.exe
- Release 솔루션 빌드: 경고 0 / 오류 0.
- 회귀 테스트 89개 assertion 통과. 가짜 HTTP/소켓과 실제 WPF·Windows 위치 고정을 사용하며 실제 증권사 인증은 하지 않음.
- 실제 공개 시세 smoke 4개 통과: 공개 기본 목록, 자동완성, 현재가·기준 시각, 제공처 pollingInterval 확인. 키·인증 헤더 없음.
- 2026-10-31 장 마감 공개 응답은 pollingInterval=70000(70초). 기본 확인 주기 2초보다 긴 제공처 간격을 우선함. 실제 1~2초 체결 갱신은 보장하지 않음.
- 팝업 생성 당시 이름 표시 여부를 캡처. 이후 체크박스 변경은 기존 창에 전달하지 않음. 서로 다른 창에서 이름 표시/숨김 동시 유지 및 동일 종목 교체 검증.
- 검색 창 286×80 DIP. 체크박스와 X를 같은 가로줄에 배치. 가격 창은 높이 32 DIP 한 줄.
- 우클릭 1회는 고정 토글, 우클릭 더블클릭은 닫기. ContextMenu 제거. 네이티브 고정 이동 차단과 종료 시 구독 제거 검증.
- 공개 현재가 캐시/배치 공유, 2초 모의 갱신, 제공처 70초 간격, HTTP 429 Retry-After, 기준 시각 역전 차단 검증.
- 최종 EXE를 실제 실행해 키 없이 공개 가격 표시, 체크박스 위치, 기존 팝업 이름 설정 유지, 새 팝업에 체크 값 반영을 컴퓨터 사용 도구로 확인함.
- UI 렌더 결과: artifacts/ui/compact-search.png, quote-fixed.png, quote-price-only.png, settings.png.
- 최종 EXE 크기: 64897442 bytes.
- 루트와 publish EXE SHA256 일치: C4DBC5533FF387DDB1F75140657EC11DF114B95B02C1556593E7C737DEE0587F
- 기존 STOCK 프로젝트 변경 없음. 이전 저장 키를 읽거나 삭제하지 않음.

## GitHub Actions Release · 2026-10-31

- v1.0.0 정식 Release 게시 완료: https://github.com/BaDook2/C-_STOCK_Viewer/releases/tag/v1.0.0
- Actions 성공: https://github.com/BaDook2/C-_STOCK_Viewer/actions/runs/41154836449
- Windows hosted runner에서 Release 빌드, 오프라인 회귀 검증 89개, 런타임 포함 단일 파일 게시, EXE만 있는 폴더에서 5초 시작 유지 확인을 통과함.
- Release 첨부는 StockViewer.exe 한 개이며 크기는 64944526 bytes.
- 첨부 SHA256과 Release 본문 체크섬 일치: 1D9C44FA3A32DC368BB8AA1AC81783DFC61DB82BD69B5999E5BB924363349989
- 최신 EXE 직접 다운로드 주소 HTTP 200 확인: https://github.com/BaDook2/C-_STOCK_Viewer/releases/latest/download/StockViewer.exe
- 배포 워크플로·게시 스크립트·버전 태그 사용 방법은 docs/Release.md에 기록함.

## 검색어 재입력 충돌 수정 · 2026-11-02

- GitHub v1.0.0의 실제 StockViewer.exe를 내려받아 기존 SHA256과 일치함을 확인한 후 실행함.
- 재현: 검색어 입력 → Ctrl+A → Backspace로 전부 삭제 → `큐리` 재입력. 실제 EXE가 종료되었고 Windows .NET Runtime 로그에 `ObjectDisposedException` / `MainWindow.RefreshSuggestions` 155행 / `CancellationTokenSource.Cancel()`이 기록됨. 2글자 제한이나 특정 종목의 공개 검색 응답 오류가 아님.
- 원인: 빈 검색어에서 취소 객체를 Dispose한 뒤 필드 참조를 남겨 다음 입력·포커스 이벤트·종료에서 다시 Cancel함.
- 수정: CancelPendingSearch에서 참조와 대기 작업을 먼저 비운 뒤 한 번만 Cancel/Dispose. 입력 갱신·종목 선택·서비스 전환·종료에서 같은 정리를 사용함. 빈 원격 응답의 선택 인덱스도 -1로 유지함.
- Release 빌드 경고 0 / 오류 0. 오프라인 검증 107개 통과.
- 새 회귀 검증: 삼성 검색 후 삭제·포커스 이동·큐리/삼성전자/NAVER/005930/SK하이닉스/한화에어로스페이스 재입력, 공백 검색 뒤 재입력, 큐리 원격 자동완성과 Enter로 팝업 열기, 빈 결과, 네트워크 실패 후 복구, 취소된 응답 무시, 검색어 삭제 직후 종료.
- v1.0.1 Release 게시 완료: https://github.com/BaDook2/C-_STOCK_Viewer/releases/tag/v1.0.1
- Actions 성공: https://github.com/BaDook2/C-_STOCK_Viewer/actions/runs/41372141454. Windows 러너에서 107개 검증 및 EXE 단독 시작 확인 통과.
- 실제 Release EXE를 다시 내려받아 삼성전자 입력 → Ctrl+A / Backspace → 큐리 재입력 → 큐리언트(4글자) 입력 → Enter를 수행함. 앱 종료 없이 자동완성과 실제 공개 가격·등락률 팝업을 확인했고 X로 정상 종료함.
- 배포 EXE 크기 64944713 bytes, 제품 버전 1.0.1+3c0b7273c9633a96831b96b85482855b2770c945.
- SHA256: EC259387E15E89679747C03B71528F2994960FE300DABF88560B7C1054BF0F47. GitHub 첨부 digest와 일치하며 프로젝트 루트와 publish의 EXE도 이 파일로 갱신함.

## 검색 입력칸 겹침과 검색 창 독립 종료 · 2026-11-02

- v1.0.1 실제 EXE에서 검색 직후 중앙의 가격 팝업이 검색 입력칸을 덮는 것을 확인함. 네트워크 조회 중에도 입력창 IsEnabled는 유지됨.
- 가격 팝업을 검색 창 아래 4 DIP 간격으로 배치하고 화면 하단에서는 위로 배치함. 가로 중앙 정렬과 현재 모니터 작업 영역 제한은 유지함.
- 검색 창을 실제 Close한 뒤에도 가격 팝업과 공통 시세 수신을 유지함. App은 SessionEnded를 받은 뒤 종료하며, 이 이벤트는 검색 창과 마지막 가격 팝업이 닫히고 서비스 정리가 끝난 뒤 발생함.
- Release 빌드 경고 0 / 오류 0, 오프라인 회귀 검증 116개 통과.
- 신규 검증: 초기 팝업의 검색창 비겹침, 화면 하단 위쪽 배치, 검색창 종료 후 주기적 갱신, 초기 시세 조회 중 검색창 종료 및 응답 완료, 종료 후 실시간 이벤트 전달, 마지막 가격 팝업 종료 시 요청 취소·서비스 Dispose, 가격 팝업이 없는 검색창 종료.
- v1.0.2 Release 게시 완료: https://github.com/BaDook2/C-_STOCK_Viewer/releases/tag/v1.0.2
- Actions 성공: https://github.com/BaDook2/C-_STOCK_Viewer/actions/runs/41374596467. Windows 러너에서 116개 검증과 EXE 단독 시작 확인 통과.
- 실제 Release EXE를 내려받아 삼성전자 가격 팝업을 연 뒤 입력칸을 한 번 클릭하고 즉시 `우`를 입력해 `삼성전자우`로 편집됨을 확인함.
- 실제 EXE에서 가격 팝업이 없을 때 X로 프로세스가 종료됨을 확인함. 가격 팝업이 있을 때는 X로 검색 창이 사라져도 가격 세션 프로세스가 응답 상태로 유지됨을 확인함. 종료 후 시세 갱신·마지막 팝업 종료 시 서비스 정리는 위 WPF 회귀 검증에서 확인함.
- 배포 EXE 크기 64945553 bytes, 제품 버전 1.0.2+530edeb4cf614e7bdbb87c84d0b7cd449cf9116c.
- SHA256: C284877693FD38C6D7975AE3AB43A3310F233BD59057D779AEA7DD982C5F940A. GitHub 첨부 digest와 일치하며 프로젝트 루트·publish의 EXE도 이 파일로 갱신함.

## 톱니바퀴 제거와 X 입력 경로 검증 · 2026-11-02

- 검색 창의 톱니바퀴와 설정창 코드·진입점을 제거함. 창에는 표시 체크박스·최소화·X·검색 입력·검색 버튼만 남김.
- 자동완성의 마우스 캡처를 없애고, X는 첫 누름에 처리하며 창 이동용 빈 영역을 버튼과 분리함.
- 기존 수명 검증의 직접 Close() 호출을 실제 CloseButton.Click 이벤트로 변경함. X 클릭 후 Win32 IsWindow로 검색 창 핸들이 파괴되고 가격 창 핸들은 유지됨을 확인함.
- 검색 응답 대기 중 X를 누른 뒤 늦게 빈 응답이 오면 자동완성의 IsOpen을 다시 true로 요청하는 경로를 추가 테스트에서 확인함. 검색 대기 전후와 종목 선택 진입 시 searchClosed를 확인해 차단함.
- 자동완성이 열린 상태의 X, 고정 가격 팝업 유지·갱신, 초기 조회 중 X, 가격 팝업 없이 X, 지연 검색 응답 후 검색 UI가 닫힌 상태 유지 등을 검증함.
- 최종 Release 빌드 경고 0 / 오류 0, 오프라인 회귀 검증 119개 통과.
- v1.0.3 Release 게시 완료: https://github.com/BaDook2/C-_STOCK_Viewer/releases/tag/v1.0.3
- Actions 성공: https://github.com/BaDook2/C-_STOCK_Viewer/actions/runs/41395590042. Windows 러너에서 119개 검증 및 EXE 단독 시작 확인 통과.
- 실제 Release EXE에서 톱니바퀴가 없고 최소화·X·검색만 버튼으로 남는 것을 화면·접근성 트리로 확인함. 자동완성의 삼성전자 행을 실제 마우스로 눌러 종목을 연 뒤, 검색칸을 다시 눌러 자동완성을 연 상태에서 X를 한 번 눌렀을 때 검색 창이 사라지고 가격 세션 프로세스가 응답 상태로 유지됨을 확인함.
- 새 실행에서는 가격 팝업 없이 X를 한 번 눌러 EXE 프로세스가 종료됨을 확인함. 테스트용 가격 세션은 확인 후 정리함.
- 배포 EXE 크기 64941546 bytes, 제품 버전 1.0.3+cc4df6114e41b6cee48b34cd3c4576f45b611d18.
- SHA256: 52479198ADABD4203A5FE89489948419382AD35F8124B1449A8364FFB3782C0E. GitHub 첨부 digest와 일치하며 프로젝트 루트·publish의 EXE도 이 파일로 갱신함.

## 우클릭 더블클릭 종료의 뒤쪽 클릭 전달 차단 · 2026-11-02

- 기존 PreviewMouseRightButtonDown에서 두 번째 누름 즉시 Close하던 동작을 제거함. 버튼을 떼는 이벤트가 끝날 때까지 가격 창과 네이티브 마우스 캡처를 유지함.
- PreviewMouseRightButtonUp을 Handled 처리하고 현재 입력 메시지 처리가 끝난 뒤 DispatcherPriority.Background에서 Close함. 가격 창의 WM_CONTEXTMENU도 소비하며, 닫을 때 캡처를 정리함.
- Release 빌드 경고 0 / 오류 0, 오프라인 회귀 검증 129개 통과.
- Fix/Free 각각의 시작 상태에서 단일 클릭 전환·Up 소비·캡처 해제, 두 번째 Down 직후 창/네이티브 HWND와 GetCapture 유지, 두 번째 Up 처리 직후 창 유지, 다음 Dispatcher 단계에서 HWND 제거와 캡처 해제를 확인함. 기존 코드에서는 Up 소비 검증이 실패함을 먼저 확인함.
- 테스트의 기존 우클릭 호출도 Down/Up 쌍으로 변경함. 검색 창을 닫은 뒤 마지막 가격 팝업 종료 시 시세 서비스 정리까지 기존 회귀 검증이 통과함.
- 수동 확인용 테스트 창은 `dotnet StockViewer.Tests/bin/Release/net10.0-windows/StockViewer.Tests.dll --right-click-check`로 열 수 있음. 팝업 뒤의 Up 수신 수·메뉴 열림 수와 닫을 때 버튼이 눌린 상태였는지를 표시함. 컴퓨터 사용 도구는 오른쪽 더블클릭을 직접 지원하지 않아, 버튼 수명과 두 번째 Up 종료는 위 WPF/Win32 회귀 검증으로 확인함.
- 실제 마우스 단일 우클릭을 가격 팝업에 보낸 후 뒤쪽 창의 Up·메뉴 열림 수가 0을 유지함을 확인함. 뒤쪽 테스트 창에 별도로 우클릭하면 Up·메뉴 열림이 각각 1이 되어 일반 우클릭 메뉴가 유지됨도 확인함.
- v1.0.4 Release 게시 완료: https://github.com/BaDook2/C-_STOCK_Viewer/releases/tag/v1.0.4
- Actions 성공: https://github.com/BaDook2/C-_STOCK_Viewer/actions/runs/41401077348. Windows 러너에서 129개 검증과 단일 EXE 실행 확인을 통과함.
- 실제 첨부 EXE를 내려받아 GitHub digest와 체크섬이 일치함을 확인하고 프로젝트 루트·publish EXE를 교체함. 크기 64942137 bytes, 버전 1.0.4+f416f46644597d56b9f8feea5d3081731f8b51a5.
- SHA256: 7FF99E6963B94B8F11A54F552F62CA634974179496BD35F64EF3B5B705337E80.

## 가격 팝업 종료 후 자동완성 재개방 방지 · 2026-11-02

- 원인: GotKeyboardFocus의 SearchFocused가 RefreshSuggestions를 호출하여, 기존 종목명이 남은 검색칸에 포커스가 돌아오면 목록을 다시 열었음.
- 가격 팝업을 Down/Up 우클릭 쌍으로 닫고 검색 창을 활성화해 포커스를 복귀시키는 회귀 테스트에서 기존 코드의 목록 재개방을 확인함.
- 포커스 이벤트의 열기 처리를 제거하고 입력·입력칸 왼쪽 클릭·방향키에서만 명시적으로 열도록 변경함. 배경 카탈로그 갱신은 닫힌 목록을 열지 않고 활성 창·입력칸 포커스 조건도 확인함.
- Release 빌드 경고 0 / 오류 0, 오프라인 회귀 검증 137개 통과.
- 추가 검증: 우클릭 종료 후 목록 닫힘, 포커스 이동·복귀 시 목록과 원격 요청 재시작 없음, 명시적 왼쪽 클릭·방향키·새 입력의 재개방 유지, 늦은 원격 응답의 닫힌 목록 재개방 방지. 기존 X 버튼·시세 수명·우클릭 Up 소비·캡처 정리도 통과함.
- v1.0.5 Release 게시 완료: https://github.com/BaDook2/C-_STOCK_Viewer/releases/tag/v1.0.5
- Actions 성공: https://github.com/BaDook2/C-_STOCK_Viewer/actions/runs/41407122827. Windows 러너에서 137개 검증과 단일 EXE 실행 확인을 통과함.
- 첨부 EXE를 내려받아 GitHub digest와 SHA256 일치를 확인하고 프로젝트 루트·publish 파일을 교체함. 크기 64942227 bytes, 버전 1.0.5+4a530c7e4fc977e142af3f473b47f37352b38490.
- SHA256: DAD3CF9156D582B33292E1B9B8EF62205939572E3508881DB6156B0D25E4063F.

## 키 없는 미국 주식·ETF 지원 · 2026-11-02

- 기존 국내 시세와 한글 검색을 유지하고 API 키 입력 없이 사용할 수 있도록 네이버 공개 검색·worldstock 시세 경로를 선택함. Alpaca 공식 문서에서 인증 키 필요, 무료 IEX 단일 거래소와 구독형 통합 SIP의 차이를 비교함. 자세한 출처와 제약은 docs/PublicData.md에 기록함.
- 미국 검색의 reutersCode를 그대로 보존하고 내부 `US:` 네임스페이스를 적용함. AAPL.O·SPY·BRKb와 6글자 티커의 경로 분기, 국내 코드와 충돌 방지, 표시 티커의 공백·대소문자 검색, 기존 목록 캐시 호환을 확인함.
- USD 가격은 최소 소수점 두 자리·최대 네 자리로 표시하며 기존 286×80 검색창·높이 32 가격 팝업을 유지함. 이름 표시의 팝업별 적용·같은 종목 교체·국내/미국 동시 팝업·검색창 종료 후 미국 가격 갱신·마지막 팝업 종료를 WPF 회귀 검증으로 확인함.
- 정규장·프리마켓·애프터마켓과 주말/DST 변경의 기준 시각을 검증함. 장후 자체 등락률을 그대로 쓰지 않고 전일 종가 기준으로 재계산함. 오래된 연장 시세·잘못된 가격·알 수 없는 통화·장후 계산 기준 누락은 유효한 정규장 값 유지 또는 오류로 처리함.
- 국내 2초/미국 70초의 독립 갱신, 시장별 배치·중복 제거·캐시·마지막 구독 종료를 확인함. 한 시장의 오류/종목 누락 시 다른 시장의 값이 전달됨을 검증함. InvalidDataException을 검색·시세의 오류 처리에 포함하여 누락 응답으로 앱이 종료되지 않게 함. HTTP 429 Retry-After 회귀 검증도 통과함.
- Release 빌드 경고 0 / 오류 0, 오프라인 회귀 검증 173개 통과. 이전 검색창 포커스·한글 검색·우클릭 Up 소비·Fix/Free·X 종료 회귀 검증도 포함함.
- 실제 수정 코드의 --public-smoke 8개 검증 통과: 국내 목록/시세, AAPL/애플의 동일 식별자, 미국 AAPL·SPY·BRK B의 양수 USD 가격과 기준 시각을 확인함. API 키·증권사 인증·주문 요청은 사용하지 않음.
- 실제 미국 응답의 시장 상태는 PREOPEN, pollingInterval은 70초였음. 장중 1~2초 체결 반영과 주간/24시간 전체 거래 지원은 검증하거나 보장하지 않음.
- artifacts/ui/quote-us-named.png 및 quote-us-price-only.png를 렌더링하고 직접 확인함. 미국 팝업은 종목명·달러 가격·등락률을 한 줄로 표시하고 이름을 숨기면 너비가 줄어듦.
- 로컬 v1.1.0 self-contained 단일 파일 게시 성공. EXE만 복사한 별도 폴더에서 인접 DLL·런타임 없이 5초 이상 시작 상태를 유지함을 확인하고 테스트 프로세스만 종료함.
- v1.1.0 Release 게시 완료: https://github.com/BaDook2/C-_STOCK_Viewer/releases/tag/v1.1.0
- Actions 성공: https://github.com/BaDook2/C-_STOCK_Viewer/actions/runs/41414406246. 동일 커밋의 Windows 빌드·오프라인 회귀 검증·EXE 단독 시작 확인과 Release 첨부가 통과함.
- 실제 Release 첨부 EXE를 내려받아 GitHub digest·파일 크기·버전과 일치함을 확인함. EXE만 있는 폴더에서 다시 5초 시작 확인을 통과하고 프로젝트 루트·publish 실행 파일도 같은 파일로 교체함. 크기 64944653 bytes, 버전 1.1.0+3213b176ecd27074fdbf538e6ee74cc5ebbd9401.
- SHA256: FD2255BF45B5DC5E6EA67BECAF16FF484FE6BC2CFD88B6A16C0C4063AAFAEEE3.

## 가격 팝업 좌우 동시 클릭으로 검색 창 표시 · 2026-11-04

- 가격 창의 좌우 버튼 동시 누름을 검색 창 표시 요청으로 처리함. 누르는 순서·떼는 순서에 관계없이 두 Up을 캡처·소비하고, 마지막 Up 이후 DispatcherPriority.Background에서 SearchRequested를 한 번 전달함.
- 단일 우클릭의 Fix/Free는 Up에서 전환하여 동시 클릭이 우선되도록 함. 진행 중인 우클릭 더블클릭 종료도 동시 클릭이면 취소함. 동시 클릭 뒤 첫 우클릭의 오래된 ClickCount, 캡처 손실, 한 버튼이 이미 눌린 진입, 실제 XAML PreviewMouseDown/Up 연결을 검증함.
- 왼쪽 드래그의 모달 루프를 제거하여 드래그 도중 오른쪽 누름도 인식함. 최소 이동 거리는 DIP로 판정하고 소유 HWND의 픽셀 시작 위치에 이동량을 적용함. 고정 상태의 드래그 금지와 기존 WM_WINDOWPOSCHANGING 이동 차단은 유지함.
- 가격 팝업이 남아 있을 때 검색 창 X는 Closing 취소와 Hide로 UI만 숨김. 같은 Window를 재표시하므로 검색어·이름 체크박스·서비스·가격 상태를 보존함. 최소화도 복원하며 Loaded 연결은 한 번만 실행함. 숨김 상태에서 마지막 가격 창을 닫으면 서비스 정리 뒤 보존한 검색 창도 실제 Close함.
- 검색 창은 클릭한 가격 창 아래 4 DIP에 가로 중앙 정렬하며 아래 작업 공간이 부족하면 위에 배치함. 기존 가격 창 배치와 동일한 모니터 작업 영역 계산을 공유함. 아래/위 배치와 음수 좌표 모니터의 좌우 경계 계산을 검증함.
- 검색 창 숨김 때 가시성 버전을 증가시켜 이전 검색 제출이 재표시 이후 자동완성이나 가격 창을 열지 못하게 함. 검색 창 재표시만으로 자동완성은 펼치지 않음.
- Release 빌드 경고 0 / 오류 0. WPF/Win32 오프라인 회귀 검증 241개 통과. 기존 국내·미국 시세·검색·이름 표시·X·우클릭 Up 소비·세션 종료 검증과 새 동시 클릭 조합·Free 드래그·Fix 유지·검색 창 재사용·최소화 복원·지연 검색 응답 검증을 포함함.
- v1.1.1 Release 게시 완료: https://github.com/BaDook2/C-_STOCK_Viewer/releases/tag/v1.1.1
- Actions 성공: https://github.com/BaDook2/C-_STOCK_Viewer/actions/runs/41849712695. Windows 러너의 로그에서 241개 검증 통과를 확인했으며, 단일 EXE 시작 확인과 Release 첨부도 통과함.
- 실제 첨부 EXE를 내려받아 GitHub digest·크기·제품 버전을 확인하고, EXE만 있는 폴더에서 5초 시작 확인을 통과함. 바탕화면 프로젝트 루트와 publish 실행 파일을 이 배포본으로 교체하고 두 경로의 체크섬 일치를 확인함. 크기 64947071 bytes, 버전 1.1.1+660df7ab352711828f685745601c4fdd71442f5f.
- SHA256: 274D42021A27F8E4535C3371F55B3DD5B7F60231440134A06A77A6DE499DCF2E.

## 1초 조회와 가격 갱신 중단 조건 수정 · 2026-11-05

- 기존 QuoteWindow에서 체결 시각이 있는 공개 가격 다음에 시각이 빠진 새 가격을 받으면 무시되는 문제를 --quote-refresh-check로 먼저 재현함. 공개 시세와 레거시 브로커 스냅샷의 판정을 분리하고, 마지막 확인한 체결 시각과 실제 수신 시각을 별도로 보존해 유효한 새 가격을 반영함. 오래된 시세·캐시는 최신 가격을 덮지 않으며, 수신은 계속되는 경우 툴팁 수신 시각이 갱신됨.
- 기존 2초 최소값과 제공처의 7초/70초 안내 값 대기를 1초 목표로 변경함. pollingInterval 원문은 참고 정보로 보관하고 요청 시작 시점을 기준으로 다음 조회를 예약함. 실제 서버 제한인 HTTP 429 및 Retry-After는 준수하고 캐시 재검증 헤더를 사용함.
- 2026-11-05 1초 간격의 소규모 공개 응답 확인에서 삼성전자의 실제 가격·기준 시각이 각 응답마다 바뀌는 것을 확인함. 장 마감 미국 시세는 가격이 같을 수 있으므로 조회 주기와 실제 체결 변화는 구분함.
- 국내·미국의 작업·게이트·캐시를 분리하고 HTTP/JSON 처리를 UI 바깥에서 수행함. 한 시장이 지연돼도 다른 시장의 정기·초기 조회를 계속함. HTTP 제한 시간은 12초에서 5초로 줄이고 서비스 종료 시 호출자 토큰 없는 초기 조회도 취소함.
- 종목 누락·일반 오류는 해당 종목만 1·2·4·8·10초로 재시도하고 정상 종목은 1초 목표를 유지함. ConnectionStatus.Code를 추가해 실패한 팝업만 흐려지고 성공 시 자동 복구되게 함. 모든 가격 팝업이 닫히면 양쪽 조회를 중지함.
- Release 빌드 경고 0 / 오류 0, 전체 오프라인 회귀 검증 266개 통과. 시장 지연 격리·정상 가격 변화·스냅샷 독립·누락 종목 재시도와 자동 복구·Retry-After 준수/해제·수신 시각·오래된 데이터 차단·종목별 연결 상태·서비스 종료 취소를 포함함. 기존 검색·우클릭·좌우 동시 클릭·검색 창 수명·국내/미국 표시 검증도 통과함.
- 실제 --public-smoke 검증 10개 통과. 국내·미국 검색·가격 조회와 두 시장의 연속 응답 각 3개를 확인함. 평균 수신 간격은 삼성전자 1.030초, 애플 1.009초였음. 실제 체결 가격이 매초 변한다는 보장은 아니며 네트워크 지연·요청 제한은 별도로 적용함.
- v1.1.2 Release 게시 완료: https://github.com/BaDook2/C-_STOCK_Viewer/releases/tag/v1.1.2
- Actions 성공: https://github.com/BaDook2/C-_STOCK_Viewer/actions/runs/41959294016. Windows 러너 로그에서 266개 회귀 검증 통과를 확인했고 단일 EXE 시작 확인과 Release 첨부도 통과함.
- 실제 첨부 EXE를 내려받아 GitHub digest·크기·제품 버전 일치를 확인하고, EXE만 있는 폴더에서 5초 시작 확인을 통과함. 바탕화면 프로젝트 루트와 publish 실행 파일을 이 배포본으로 교체하고 체크섬 일치를 확인함. 크기 64950421 bytes, 버전 1.1.2+e45d080e99425e8233d21cab93e8bdd4cdcebace.
- SHA256: 49CBF5284536FB3B151B6C535C936829D2CE4614800737E2D9A7AC5E3FFBB379.
