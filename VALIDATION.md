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
