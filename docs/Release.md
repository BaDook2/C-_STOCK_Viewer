# GitHub Actions 단일 EXE 배포

일반 사용자는 [최신 StockViewer.exe](https://github.com/BaDook2/C-_STOCK_Viewer/releases/latest/download/StockViewer.exe) 하나만 다운로드해 실행합니다. 소스 코드 ZIP, DLL, .NET 설치, 증권사 API 키는 필요 없습니다. Windows x64용이며 공개 시세를 조회하므로 인터넷은 필요합니다.

v1.0.1은 검색어를 전부 지운 뒤 새 종목을 입력하거나 검색창에 다시 포커스를 줄 때 종료되는 문제를 수정합니다. v1.0.0을 사용 중이면 EXE를 새 버전으로 교체하세요.

v1.0.2는 가격 팝업을 검색 창 아래(공간 부족 시 위)에 배치해 입력칸을 가리지 않게 합니다. 검색 창의 X는 가격 팝업을 유지하며, 마지막 가격 팝업을 닫으면 시세 연결을 정리하고 종료합니다.

v1.0.3은 톱니바퀴와 설정창을 제거합니다. 자동완성의 마우스 캡처와 상단 창 이동 영역을 정리해 X의 첫 누름에서 검색창을 닫도록 하고, 닫은 뒤의 지연 검색 응답이 검색 UI를 다시 열지 않도록 합니다.

v1.0.4는 가격 팝업을 우클릭 두 번으로 닫을 때 뒤쪽 창에 버튼을 떼는 이벤트가 전달되는 문제를 수정합니다. 팝업이 Up까지 캡처·소비한 뒤 닫히며, 단일 우클릭의 Fix/Free 전환은 유지됩니다.

v1.0.5는 가격 팝업을 닫은 뒤 검색칸으로 포커스가 복귀할 때 자동완성이 저절로 펼쳐지는 문제를 수정합니다. 새 입력·입력칸 왼쪽 클릭·방향키로 요청할 때만 목록을 엽니다.

v1.1.0은 API 키 없이 미국 주식·ETF를 한글 이름이나 티커로 검색하고 달러 가격을 표시합니다. 국내·미국 팝업을 함께 사용할 수 있으며 최신 프리마켓·애프터마켓 가격이 제공되면 전일 대비 등락률을 계산해 표시합니다. 시장별 제공처 갱신 간격을 따르며 기존 창 크기와 조작 방식은 유지합니다.

v1.1.1은 가격 팝업의 좌우 버튼 동시 클릭으로 검색 창을 재표시하거나 이동합니다. 기본은 가격 창 아래이며 아래 공간이 부족하면 위에 표시합니다. 숨김·최소화 상태도 복원하고 기존 검색어와 이름 표시 설정은 유지합니다. 두 버튼을 모두 뗄 때까지 캡처하며 고정 전환·더블클릭 종료보다 우선 처리합니다.

## 새 버전 배포

변경 사항을 main에 커밋·푸시한 뒤 새 버전 태그를 올립니다. 예:

```powershell
git push origin main
git tag -a v1.1.1 -m "Release v1.1.1"
git push origin v1.1.1
```

`v숫자.숫자.숫자` 형식만 지원합니다. 이미 게시한 버전은 수정하거나 덮어쓰지 않고 다음 버전 번호를 사용합니다.

`.github/workflows/release.yml`은 Windows에서 .NET 10 설치 → Release 빌드 → 네트워크 없는 회귀 검증 → self-contained 단일 파일 게시 → EXE 하나만 복사한 폴더에서 시작 확인을 실행합니다. 모두 통과하면 별도 권한의 job이 Release에 StockViewer.exe를 첨부합니다. SHA-256은 Release 본문에 기록합니다.

Actions의 `Release Windows EXE` → `Run workflow`에서도 기존 버전 태그를 입력해 실행할 수 있습니다. 실패한 버전은 재실행할 수 있지만 이미 게시된 Release를 덮어쓰지는 않습니다. 테스트 화면은 실행의 WPF-diagnostics artifact에서 확인할 수 있습니다.

GitHub가 자동 발급하는 GITHUB_TOKEN을 사용하므로 별도 PAT나 증권사 키를 Secrets에 등록하지 않아도 됩니다. GitHub 저장소에서 Actions 실행과 Release 쓰기 권한이 허용되어 있어야 합니다.

## 같은 방식으로 로컬 게시

```powershell
./Publish.ps1 -Version 1.1.1
./Publish.ps1 -Version 1.1.1 -OutputDirectory artifacts/release -SkipRootCopy
```

기본 호출은 publish에 만든 EXE를 프로젝트 루트에도 복사합니다. SkipRootCopy는 실행 중인 루트 EXE를 건드리지 않고 별도 경로에 게시할 때 사용합니다. WPF는 트리밍하지 않으며 런타임·네이티브 라이브러리를 단일 EXE에 포함하고 압축합니다.
