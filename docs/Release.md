# GitHub Actions 단일 EXE 배포

일반 사용자는 [최신 StockViewer.exe](https://github.com/BaDook2/C-_STOCK_Viewer/releases/latest/download/StockViewer.exe) 하나만 다운로드해 실행합니다. 소스 코드 ZIP, DLL, .NET 설치, 증권사 API 키는 필요 없습니다. Windows x64용이며 공개 시세를 조회하므로 인터넷은 필요합니다.

v1.0.1은 검색어를 전부 지운 뒤 새 종목을 입력하거나 검색창에 다시 포커스를 줄 때 종료되는 문제를 수정합니다. v1.0.0을 사용 중이면 EXE를 새 버전으로 교체하세요.

## 새 버전 배포

변경 사항을 main에 커밋·푸시한 뒤 새 버전 태그를 올립니다. 예:

```powershell
git push origin main
git tag -a v1.0.2 -m "Release v1.0.2"
git push origin v1.0.2
```

`v숫자.숫자.숫자` 형식만 지원합니다. 이미 게시한 버전은 수정하거나 덮어쓰지 않고 다음 버전 번호를 사용합니다.

`.github/workflows/release.yml`은 Windows에서 .NET 10 설치 → Release 빌드 → 네트워크 없는 회귀 검증 → self-contained 단일 파일 게시 → EXE 하나만 복사한 폴더에서 시작 확인을 실행합니다. 모두 통과하면 별도 권한의 job이 Release에 StockViewer.exe를 첨부합니다. SHA-256은 Release 본문에 기록합니다.

Actions의 `Release Windows EXE` → `Run workflow`에서도 기존 버전 태그를 입력해 실행할 수 있습니다. 실패한 버전은 재실행할 수 있지만 이미 게시된 Release를 덮어쓰지는 않습니다. 테스트 화면은 실행의 WPF-diagnostics artifact에서 확인할 수 있습니다.

GitHub가 자동 발급하는 GITHUB_TOKEN을 사용하므로 별도 PAT나 증권사 키를 Secrets에 등록하지 않아도 됩니다. GitHub 저장소에서 Actions 실행과 Release 쓰기 권한이 허용되어 있어야 합니다.

## 같은 방식으로 로컬 게시

```powershell
./Publish.ps1 -Version 1.0.1
./Publish.ps1 -Version 1.0.1 -OutputDirectory artifacts/release -SkipRootCopy
```

기본 호출은 publish에 만든 EXE를 프로젝트 루트에도 복사합니다. SkipRootCopy는 실행 중인 루트 EXE를 건드리지 않고 별도 경로에 게시할 때 사용합니다. WPF는 트리밍하지 않으며 런타임·네이티브 라이브러리를 단일 EXE에 포함하고 압축합니다.
