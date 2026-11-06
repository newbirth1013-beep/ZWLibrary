# ZWLibrary v0.8.4

ZWCAD용 전기 부품·심볼·배치도형 관리 플러그인입니다.
C# / .NET Framework 4.8 / Windows x64 / ZWCAD 2024 기준입니다.

## 구성

- main: AI·MCP 없는 v0.8.4 기준판.
- feature/chatgpt-login: 기준판에 ChatGPT 로그인을 추가한 시험판.
- 부품·심볼·배치도형 관리, 삽입, 속성 편집, 선택 PARTLIST/BOM 및 CSV 출력.
- 도면·부품 카탈로그·DWG 자산·ZWCAD SDK DLL은 제공하지 않습니다.

## 빌드와 실행

.NET Framework 4.8 개발용 참조 어셈블리 및 정상 설치된 ZWCAD 2024가 필요합니다.

```bat
BUILD_WINDOWS.cmd "C:\Program Files\ZWSOFT\ZWCAD 2024" --no-pause
TEST_UI_WINDOWS.cmd
```

결과 DLL: `output/v0.8.4/plugin/ZWLibrary.dll`.
새 ZWCAD 세션에서 NETLOAD로 로드하고 ZLIBHELLO로 버전을 확인합니다. 다른 버전의 같은 이름 DLL이 이미 로드되었다면 재시작이 필요합니다.
ZLIB는 관리 창, ZLIBSCAN은 구조 조회 명령입니다.

UPDATE_WINDOWS.cmd와 BUILD_AND_UPDATE.cmd는 실행 시 프로젝트의 runtime 폴더를 갱신합니다.
실제 도면은 사본으로 시험하고, 재배포 권한을 가진 자산만 등록하세요.

## 검증과 제한

핵심 검사 249개와 UI 검사 48개 통과. 기준판 업데이트 검사는 실행 환경 호환 문제로 완료하지 못했습니다.
로그인 개발 브랜치에는 해당 호환 수정이 포함되어 있습니다.
실제 CAD 입력 지연과 삽입·취소·재열기 검증은 별도입니다.
이 기준판에는 이후 버전의 개선이 포함되어 있지 않습니다.

## 라이선스

자체 작성 코드는 MIT LICENSE를 따릅니다. ZWCAD/SDK와 외부 도면·제조사 자료의 권리는 포함하지 않습니다.
