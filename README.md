# StockViewer

국내·미국 주식과 ETF의 가격을 작은 한 줄 팝업으로 보여주는 **Avalonia 데스크톱 앱**입니다. 차트와 복잡한 정보 대신 종목명·현재가·전일 대비 등락률을 간단하게 확인할 수 있습니다.

**[최신 StockViewer.exe 다운로드](https://github.com/BaDook2/C-_STOCK_Viewer/releases/latest/download/StockViewer.exe)**

Windows x64에서는 실행 파일 하나만 받아 실행하면 됩니다. 별도 설치, .NET 설치, 증권사 API 키 입력이 필요하지 않습니다. 시세를 조회하려면 인터넷 연결이 필요합니다.

## 사용 방법

1. `삼성전자`, `애플`, `AAPL`, `테슬라`, `SPY`, `BRK B`처럼 국내 종목 이름·코드·초성이나 미국 종목 이름·티커를 입력합니다.
2. 자동완성 결과를 클릭하거나 검색 버튼·Enter로 가격 팝업을 엽니다. 여러 종목을 함께 열 수 있습니다.
3. **종목이름 Display**는 새로 여는 팝업의 이름 표시 여부를 결정합니다. 같은 종목을 다시 열면 기존 창을 현재 설정으로 교체합니다.

검색 창은 **286×80 DIP**, 가격 창은 높이 **32 DIP**입니다. 국내 가격은 `72,500원`, 미국 가격은 `$123.45`처럼 표시합니다. 상승은 빨강, 하락은 파랑으로 표시합니다.

| 가격 팝업 조작 | 동작 |
|---|---|
| 왼쪽 드래그 | Free 상태에서 위치 이동 |
| 우클릭 한 번 | Fix/Free 전환. Fix는 항상 위에 표시하고 위치 고정 |
| 우클릭 두 번 | 해당 팝업 닫기. 버튼을 뗀 뒤 닫아 뒤쪽 창에 우클릭이 전달되지 않게 처리 |
| 좌우 버튼 동시 클릭 | 검색 창을 팝업 근처로 이동·복원. 기본은 아래, 공간이 부족하면 위 |
| Esc | 가격 팝업 닫기 |

가격 팝업이 남아 있으면 검색 창의 X는 검색 창만 숨깁니다. 좌우 동시 클릭으로 검색 창을 다시 표시할 수 있습니다. 검색 창이 숨겨진 상태에서 마지막 가격 팝업까지 닫으면 프로그램이 종료됩니다.

## 시세 갱신

네이버 공개 시세를 **1초 목표**로 조회합니다. 국내·미국 조회와 오류 재시도를 분리해 한 종목의 오류가 다른 종목을 멈추지 않게 합니다. 마우스를 가격 창에 올리면 제공처·시세 기준 시각·실제 수신 시각·연결 상태를 확인할 수 있습니다.

미국은 정규장보다 최신인 프리마켓·애프터마켓 가격이 제공되면 사용하고 전일 종가 기준 등락률을 계산합니다. 실제 가격 반영 속도는 제공처·거래 세션·통신 상태에 따라 달라집니다. HTTP 요청 제한이 발생하면 Retry-After를 준수합니다. 자세한 동작은 [시세 문서](docs/PublicData.md)에 있습니다.

표시 설정과 검색 캐시는 `%LOCALAPPDATA%\StockViewer`에 저장합니다. 기존 StockViewer 설정은 유지하며, 파일이 없으면 별도 Avalonia 시험판의 표시 설정·검색 캐시만 가져옵니다. API 키는 읽거나 이전하지 않습니다.

## v2.0.0

WPF 화면을 **Avalonia 12.1.5**로 교체하고 프로젝트·실행 파일 이름을 `StockViewer`로 통일했습니다. 기존 작은 디자인과 검색·시세·우클릭·검색 창 호출 기능을 유지합니다. 이전 버전의 실행 파일을 새 `StockViewer.exe`로 교체해 사용하세요.

현재 배포본은 Windows x64입니다. macOS/Linux 실제 실행 검증 결과를 포함하지 않습니다.

## 개발과 배포

.NET 10 SDK를 사용합니다. UI는 Avalonia이며 시세 코어와 UI가 분리되어 있습니다.

```powershell
dotnet build StockViewer.slnx -c Release
dotnet StockViewer.Tests/bin/Release/net10.0/StockViewer.Tests.dll
dotnet StockViewer.Tests/bin/Release/net10.0/StockViewer.Tests.dll --native --ui-only
# 실제 공개 검색·시세 연결 확인
dotnet StockViewer.Tests/bin/Release/net10.0/StockViewer.Tests.dll --public-smoke
./Publish.ps1
```

버전 태그를 올리면 GitHub Actions가 빌드·헤드리스 회귀 검증·Windows 창 검증·단일 EXE 시작 확인을 거쳐 Release에 `StockViewer.exe`를 첨부합니다. [배포 방법](docs/Release.md), [프로그램 구조](docs/Architecture.md), [검증 기록](VALIDATION.md)을 참고하세요.
