# StockViewer

국내·미국 주식과 ETF의 가격을 작은 한 줄 팝업으로 보여주는 **Avalonia 데스크톱 앱**입니다. 차트와 복잡한 정보 대신 종목명·현재가·전일 대비 등락률을 간단하게 확인할 수 있습니다.

최신 버전 다운로드: [릴리즈 페이지](https://github.com/BaDook2/C-_STOCK_Viewer/releases/latest)

- [Windows x64 · StockViewer.exe](https://github.com/BaDook2/C-_STOCK_Viewer/releases/latest/download/StockViewer.exe)
- [Mac Apple Silicon · 앱 ZIP](https://github.com/BaDook2/C-_STOCK_Viewer/releases/latest/download/StockViewer-macOS-arm64.zip)
- [Mac Intel · 앱 ZIP](https://github.com/BaDook2/C-_STOCK_Viewer/releases/latest/download/StockViewer-macOS-x64.zip)

Windows에서는 실행 파일을, Mac에서는 자신의 칩에 맞는 ZIP을 받아 압축을 풀고 `StockViewer-macOS.app`을 실행합니다. 별도 .NET 설치나 증권사 API 키 입력이 필요하지 않습니다. Mac 앱에는 Apple Developer ID 서명·공증이 포함되지 않아 최초 실행 시 macOS 보안 확인이 표시될 수 있습니다. 시세를 조회하려면 인터넷 연결이 필요합니다.

## 사용 방법

1. `삼성전자`, `애플`, `AAPL`, `테슬라`, `SPY`, `BRK B`처럼 국내 종목 이름·코드·초성이나 미국 종목 이름·티커를 입력합니다.
2. 자동완성 결과를 클릭하거나 검색 버튼·Enter로 가격 팝업을 엽니다. 여러 종목을 함께 열 수 있습니다. 가격 데이터가 도착한 뒤 가격창을 표시하며, 3초 동안 받지 못하면 안내 팝업을 표시합니다. 새 가격창의 위치가 다른 가격창과 겹치면 아래 또는 위의 빈자리에 자동 배치합니다.
3. **종목이름 Display**는 새로 여는 팝업의 이름 표시 여부를 결정합니다. 같은 종목을 다시 열면 기존 창을 현재 설정으로 교체합니다.

검색 창은 **286×80 DIP**, 가격 창은 높이 **32 DIP**입니다. 국내 가격은 `72,500원`, 미국 가격은 `$123.45`처럼 표시합니다. 상승은 빨강, 하락은 파랑으로 표시하며, 제공처에서 정규장과 시간외 거래가 모두 마감된 것으로 확인되면 글자를 회색으로 표시합니다. 거래가 재개되면 자동으로 등락 색상으로 돌아옵니다.

검색 창 오른쪽의 **달/해 버튼**은 라이트·다크 모드를 번갈아 전환합니다. 이미 열린 가격 팝업과 자동완성에도 즉시 반영되며 다음 실행에도 선택한 모드를 유지합니다. 최소화·닫기는 플랫폼별 글꼴 없이 벡터 아이콘으로 표시합니다.

| 가격 팝업 조작 | 동작 |
|---|---|
| 왼쪽 드래그 | Free 상태에서 위치 이동 |
| 우클릭 한 번 | Fix/Free 전환. Fix는 항상 위에 표시하고 위치 고정 |
| 우클릭 두 번 | 해당 팝업 닫기. 버튼을 뗀 뒤 닫아 뒤쪽 창에 우클릭이 전달되지 않게 처리 |
| 좌우 버튼 동시 클릭 | 검색 창을 팝업 근처로 이동·복원. 기본은 아래, 공간이 부족하면 위 |
| Esc | 가격 팝업 닫기 |

가격 팝업이 남아 있으면 검색 창의 X는 검색 창만 숨깁니다. 좌우 동시 클릭으로 검색 창을 다시 표시할 수 있습니다. 검색 창이 숨겨진 상태에서 마지막 가격 팝업까지 닫으면 프로그램이 종료됩니다. 작업표시줄의 창 닫기·Alt+F4 또는 Mac Dock의 종료는 검색창과 가격 팝업을 모두 종료하고 진행 중인 시세 요청도 취소합니다.

## 시세 갱신

네이버 공개 시세와 미국 데이마켓용 토스증권 비로그인 공개 시세를 **1초 목표**로 조회합니다. 국내·미국 조회와 오류 재시도를 분리해 한 종목의 오류가 다른 종목을 멈추지 않게 합니다. 마우스를 가격 창에 올리면 제공처·시세 기준 시각·실제 수신 시각·연결 상태를 확인할 수 있습니다.

국내는 KRX와 NXT 중 최신 시세를 사용하며 KRX 애프터마켓과 NXT 프리·애프터마켓도 갱신합니다. 미국은 데이마켓도 갱신하며, 프리·정규·애프터마켓과 비교해 최신 유효 가격을 사용합니다. 토스증권의 실제 거래일·데이마켓 운영시간 및 종목별 지원 여부를 확인하고 제공처의 기준가로 등락률을 계산합니다. 정규장만 끝났더라도 시간외 거래가 열려 있으면 등락 색상을 유지합니다. 실제 가격 반영 속도는 제공처·거래 세션·통신 상태에 따라 달라집니다. HTTP 요청 제한이 발생하면 Retry-After를 준수합니다. 자세한 동작은 [시세 문서](docs/PublicData.md)에 있습니다.

표시 설정과 검색 캐시는 `%LOCALAPPDATA%\StockViewer`에 저장합니다. 기존 StockViewer 설정은 유지하며, 파일이 없으면 별도 Avalonia 시험판의 표시 설정·검색 캐시만 가져옵니다. API 키는 읽거나 이전하지 않습니다.

## v2.1.1

마우스 hover는 글자를 강조하지 않고 배경·테두리만 변경합니다. 첫 시세가 도착한 후 가격창을 표시하며 3초 이상 미수신 시 종목별 안내를 보여줍니다. 운영체제에서 프로그램을 종료하면 가격창도 함께 종료합니다.

## v2.1.0

Windows와 Apple Silicon·Intel Mac을 함께 배포합니다. 벡터 아이콘, 즉시 반영되는 라이트/다크 모드, 미국 데이마켓·국내 시간외 시세, 마감 회색 표시, 겹치지 않는 가격창 배치를 추가했습니다. [릴리즈 변경 내용](docs/releases/v2.1.0.md)을 참고하세요.

## v2.0.0

WPF 화면을 **Avalonia 12.1.5**로 교체하고 프로젝트·실행 파일 이름을 `StockViewer`로 통일했습니다. 기존 작은 디자인과 검색·시세·우클릭·검색 창 호출 기능을 유지합니다. 이전 버전의 실행 파일을 새 `StockViewer.exe`로 교체해 사용하세요.

배포 대상은 Windows x64와 macOS arm64/x64입니다. GitHub Actions가 각 대상 운영체제에서 회귀·실제 창·압축 해제 후 시작 검증을 수행합니다. Linux는 배포 대상에 포함하지 않습니다.

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

`.github/release-version`을 변경해 올리거나 버전 태그로 실행하면 GitHub Actions가 빌드·회귀·각 OS의 실제 창과 배포 파일 시작 검증을 거쳐 Windows EXE와 Mac 앱 ZIP을 같은 Release에 첨부합니다. [배포 방법](docs/Release.md), [프로그램 구조](docs/Architecture.md), [검증 기록](VALIDATION.md)을 참고하세요.
