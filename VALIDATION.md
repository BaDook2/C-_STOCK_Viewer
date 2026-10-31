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
