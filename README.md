# STOCK MINI · StockViewer

작은 Windows 검색 창과 한 줄짜리 가격 팝업입니다. 실제 프로젝트와 실행 파일은 `C:\Users\wkdru\Desktop\C#\StockViewer`에 있습니다.

## 사용

`StockViewer.exe`를 실행하고 종목을 검색합니다. 별도 설치나 API 키 입력은 필요 없습니다. 네이버 공개 시세를 인터넷으로 조회합니다.

- 검색 창은 286×80 DIP입니다. 종목이름 Display 체크박스와 톱니바퀴·최소화·닫기가 같은 줄에 있습니다.
- 입력하면 자동완성이 열립니다. 결과 클릭, 검색 버튼, Enter로 팝업을 엽니다.
- **종목이름 Display는 새 팝업을 여는 시점에만 적용**합니다. 이미 연 팝업은 바뀌지 않으며 팝업마다 표시 여부가 다를 수 있습니다.
- 가격 창은 높이 32 DIP로 `종목명 가격 등락률`만 한 줄로 표시합니다. 이름을 숨기면 너비도 줄어듭니다.
- **우클릭 한 번: Fix/Free 전환**. 고정하면 항상 위에 표시되고 위치를 옮길 수 없습니다.
- **우클릭 두 번(더블클릭): 해당 가격 창 닫기**. Esc로도 닫습니다.
- 같은 종목을 다시 열면 기존 창을 교체하고 현재 이름 표시 설정으로 검색 창 가운데에 새로 엽니다.
- 검색 창을 최소화해도 가격 팝업은 유지됩니다. 검색 창의 X는 모든 팝업과 함께 종료합니다.
- 톱니바퀴에서는 공개 시세 상태를 확인하거나 다시 연결합니다. 키 입력란은 없습니다.

## 갱신 주기

기본 확인 주기는 **2초**이며 제공처의 `pollingInterval`이 더 길면 그 간격을 따릅니다. 따라서 실제 체결 가격의 1~2초 갱신은 보장하지 않습니다. 2026-10-31 토요일 확인 당시 장 마감 응답은 **70초**를 지정했습니다. 상세 내용과 제공처 링크는 [공개 시세 문서](docs/PublicData.md)에 있습니다.

마우스를 가격 창에 올리면 제공처·기준 날짜/시각·장 마감 여부·Fix/Free 상태를 확인할 수 있습니다. 연결이 끊기면 마지막 가격을 흐리게 표시하고 재시도합니다. 가짜 가격으로 대체하지 않습니다.

이름 표시 기본값과 검색 목록 캐시는 `%LOCALAPPDATA%\StockViewer`에 저장합니다. 이전 버전의 저장 키는 더 이상 읽거나 사용하지 않습니다.

## 개발과 검증

.NET 10 SDK와 Windows가 필요합니다. 별도 NuGet 패키지 의존성은 없습니다.

```powershell
dotnet build StockViewer.slnx -c Release
dotnet StockViewer.Tests/bin/Release/net10.0-windows/StockViewer.Tests.dll
# 실제 공개 검색·목록·시세 확인 (증권사 인증 없음)
dotnet StockViewer.Tests/bin/Release/net10.0-windows/StockViewer.Tests.dll --public-smoke
./Publish.ps1
```

기존 STOCK의 색상·글꼴·둥근 모서리를 유지했습니다. 원본 STOCK 프로젝트는 수정하지 않습니다. 이전 작업 위치에는 이동 안내를 남겼습니다.
