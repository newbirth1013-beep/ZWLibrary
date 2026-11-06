# ZWLibrary ChatGPT 로그인 시험판

v0.8.4 기반의 로그인 AI 보조 기능입니다. 어셈블리 버전은 0.8.4.1입니다.
MCP 없이 ZWCAD 내부 플러그인이 검증된 작업 제안을 실행합니다.

## 사용

1. 기존 ZWLibrary의 중복 로드를 해제하고 ZWCAD를 새로 시작합니다.
2. `output/v0.8.4-ai.1/plugin/ZWLibrary.dll`을 NETLOAD합니다.
3. 시험 도면 사본에서 ZLIBAI를 실행하고 관리 부품을 선택합니다.
4. Continue with ChatGPT로 로그인하고, 구독 사용 동의를 확인합니다.
5. 모델을 선택한 뒤 먼저 부품 설명을 요청합니다.
6. 기기번호 변경을 요청할 때는 제안을 확인한 뒤 적용 버튼을 누릅니다.

API 키와 API 결제 자동 전환은 사용하지 않습니다. 지원 플랜·앱 등록·권한·사용 한도에 따라 이용이 제한될 수 있습니다.
실제 ChatGPT 로그인, 모델 조회 및 구독 추론 응답 완료를 확인했습니다.

## 기능과 제한

- 선택한 관리 부품 최대 200개 조회, 선택 범위의 품번/번호 누락과 중복 후보 검사.
- AI가 제안한 단일 기기번호 변경. 연결 부품과 최상위 UNIT_NO가 하나가 아닌 부품은 변경 불가.
- 선택 범위, 현재 값, 같은 판넬의 관리 부품 번호 중복을 확인하고 트랜잭션 안에서 적용·재검증.
- 자동 저장 없음. 실제 CAD의 UNDO·저장 후 재열기는 별도 시험 필요.
- 자동 배선·다중 번호 변경·회로 자동 생성은 지원하지 않습니다.

## 개인정보 처리

전송 버튼은 선택 부품의 핸들·기기번호·판넬·모델·제조사·이름·연결 여부·검사 요약과 요청을 OpenAI로 보냅니다. 전체 DWG 파일은 보내지 않습니다.
로그인 정보는 Windows DPAPI로 암호화해 `%LOCALAPPDATA%/ZWLibrary/ChatGPT`에 보관합니다. 이 폴더를 공유하거나 저장소에 추가하지 마세요.
로그인은 브라우저에서 진행하며 비밀번호를 플러그인에서 받지 않습니다. 다른 프로그램의 인증정보를 가져오지 않습니다.

## 빌드와 검증

Windows x64, .NET Framework 4.8 개발용 참조 어셈블리, ZWCAD 2024가 필요합니다.

```bat
BUILD_WINDOWS.cmd "C:\Program Files\ZWSOFT\ZWCAD 2024" --no-pause
TEST_AGENT.cmd
TEST_UI_WINDOWS.cmd
```

핵심 249개 / UI 48개 / 업데이트 7개 / 인증·AI 39개: 총 343개 검사 통과.
실제 로그인·모델 조회·구독 AI 응답과 CAD 메모리 DB 검사 5개를 통과했습니다. 실제 작업 도면의 대화창 적용·UNDO·저장 후 재열기는 별도 검증 대상입니다.
자체 작성 코드는 MIT LICENSE를 따릅니다. SDK·도면·제조사 자산은 포함하지 않습니다.

공식 연동 근거:
- https://developers.openai.com/siwc/token-sharing-open-source/sign-in
- https://developers.openai.com/siwc/token-sharing-open-source/profiles-and-sessions
- https://developers.openai.com/siwc/token-sharing-open-source/models-and-inference

## ZWCAD 재시작 없이 로그인 연결 시험

`BUILD_CONNECTION_CHECK.cmd`로 `output/connection-check/ZWLibrary-ConnectionCheck.exe`를 빌드할 수 있습니다.
실행 후 Continue with ChatGPT를 누르면 기본 브라우저에서 로그인합니다.
로그인 및 모델 조회 후 모델을 선택하고 연결 확인을 누르세요. 실제 도면 정보 없이 짧은 시험 요청만 전송합니다.
로그인 정보는 플러그인과 같은 암호화 저장소를 사용합니다. 비밀정보는 상태 파일에 기록하지 않습니다.
이 도구는 도면을 열거나 수정하지 않으며, 성공해도 실제 CAD 작업 검증을 대신하지 않습니다.

## ZWCAD 메모리 DB 검증

BUILD_HOST_CHECK.ps1로 시험용 어셈블리와 로더를 빌드합니다.
ZWCAD에서 output/host-check/HostCheckLoader.dll을 NETLOAD하고 ZWLIBMEMCHECK를 실행합니다.
이 검사는 별도 메모리 DB에서 번호 변경·중복 차단·오래된 정보 차단·역변경·연결 부품 차단을 확인합니다. 활성 작업 도면은 읽거나 수정하지 않습니다.
이는 실제 도면에서의 UNDO 및 저장·재열기 시험을 대신하지 않습니다.
