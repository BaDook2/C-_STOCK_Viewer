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
