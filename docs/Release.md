# StockViewer 단일 EXE 배포

일반 사용자는 [최신 StockViewer.exe](https://github.com/BaDook2/C-_STOCK_Viewer/releases/latest/download/StockViewer.exe) 하나만 받아 실행합니다. Windows x64 자체 포함 파일이며 별도 .NET 설치나 API 키 입력이 필요하지 않습니다.

v2.0.0은 Avalonia 전환 버전입니다. 프로젝트 폴더·솔루션·실행 파일을 StockViewer로 통일하고 기존 WPF 프로젝트를 대체합니다. 이전 태그와 Release는 Git 이력으로 유지하며 최신 다운로드는 Avalonia 버전을 제공합니다.

## GitHub Actions

```powershell
git push origin main
git tag -a v2.0.0 -m "Release v2.0.0: Avalonia StockViewer"
git push origin v2.0.0
```

새 태그를 올리면 Release 빌드 → 헤드리스 회귀 검증 → 실제 Windows 창 검증 → Windows x64 단일 파일 게시 → EXE 하나만 있는 폴더의 시작 확인 → Release 첨부 순으로 진행합니다. Release 본문에 SHA256을 기록합니다.

Actions의 Release Windows EXE에서 기존 태그를 입력해 수동 실행할 수도 있습니다. 이미 게시한 Release는 덮어쓰지 않고 다음 버전 번호를 사용합니다. GitHub 기본 GITHUB_TOKEN으로 Release를 생성하므로 별도 증권사 키나 배포용 PAT 설정이 필요하지 않습니다.

## 로컬

```powershell
./Publish.ps1
./Publish.ps1 -Version 2.0.0 -OutputDirectory artifacts/release -SkipRootCopy
```

기본 호출은 publish/StockViewer.exe와 프로젝트 루트 StockViewer.exe를 만듭니다. 네이티브 라이브러리와 .NET 런타임은 단일 파일에 포함하고 PDB는 배포하지 않습니다. 실제 Windows x64 실행만 검증된 배포본입니다.
