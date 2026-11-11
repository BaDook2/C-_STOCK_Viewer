# Windows·macOS 배포

[최신 릴리즈](https://github.com/BaDook2/C-_STOCK_Viewer/releases/latest)는 다음 파일을 제공합니다.

- `StockViewer.exe`: Windows x64 단일 실행 파일
- `StockViewer-macOS-arm64.zip`: Apple Silicon Mac 앱
- `StockViewer-macOS-x64.zip`: Intel Mac 앱
- `SHA256SUMS.txt`: 세 파일의 SHA-256 체크섬

모두 .NET 런타임을 포함합니다. Mac ZIP은 압축을 풀고 `StockViewer-macOS.app`을 실행합니다. Mac 앱은 ad-hoc 서명이며 Apple Developer ID 서명·공증은 포함하지 않습니다. 처음 실행할 때 macOS의 보안 확인이 표시될 수 있습니다.

## 새 버전 게시

1. `StockViewer.App/StockViewer.App.csproj`의 Version과 `.github/release-version`을 같은 버전으로 변경합니다.
2. `Publish.ps1`과 `scripts/publish_macos.py`의 기본 버전도 맞추고 `docs/releases/v버전.md`에 릴리즈 노트를 작성합니다.
3. 변경 사항을 main에 커밋·푸시합니다. `.github/release-version` 변경이 배포를 실행합니다.

버전 태그 push 또는 Actions의 **Release Windows and macOS**에서 수동 실행할 수도 있습니다. 수동 실행의 tag는 기존 태그를 지정하거나 비워 현재 릴리즈 버전을 사용합니다. 이미 존재하는 태그가 다른 커밋을 가리키면 중단하며, 이미 게시한 Release는 덮어쓰지 않습니다.

빌드 대상은 Windows, macOS Apple Silicon, macOS Intel입니다. 각 대상이 동일한 커밋으로 복원·Release 빌드·전체 회귀·네이티브 창 검증을 수행한 뒤 배포 파일을 생성합니다. Windows는 EXE만 있는 새 폴더에서 시작을 확인하고, Mac은 ZIP을 새 폴더에 풀어 아키텍처·코드 서명·자체 포함 런타임·시작을 확인합니다. 세 대상이 모두 통과해야 하나의 GitHub Release를 게시하고 태그를 해당 커밋에 연결합니다. GitHub Actions의 GITHUB_TOKEN을 사용하므로 별도 배포 토큰은 필요하지 않습니다.

## 로컬 패키징

Windows PowerShell:

```powershell
./Publish.ps1 -Version 2.2.5 -Runtime win-x64 -OutputDirectory artifacts/release-win -SkipRootCopy
```

Mac:

```sh
python3 scripts/publish_macos.py --version 2.2.5 --runtime osx-arm64 --output artifacts/release-arm64
python3 scripts/smoke_macos.py --version 2.2.5 --runtime osx-arm64 --directory artifacts/release-arm64
```

Intel Mac에서는 `osx-x64`를 사용합니다. 출력 디렉터리는 같은 앱·ZIP·작업 페이로드가 없는 새 디렉터리를 사용합니다. `ditto`로 ZIP을 만들어 실행 권한과 코드 서명 메타데이터를 보존합니다.
