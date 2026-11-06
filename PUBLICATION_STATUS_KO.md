# 시험판 검증 상태

- v0.8.4 기반 / 어셈블리 0.8.4.1.
- 핵심 249 / UI 48 / 업데이트 7 / 인증·AI 39: 총 343개 검사 통과.
- 실제 로그인·모델 조회·구독 AI 응답과 CAD 메모리 DB 검사 5개 통과. 실제 작업 도면의 대화창 적용·UNDO·저장 후 재열기는 별도 검증 대상.
- main은 AI 없는 기준판, feature/chatgpt-login은 로그인 시험판.
- 개인 식별 이메일 대신 GitHub noreply 커밋 주소 사용.

## ZWCAD 메모리 DB 검증

BUILD_HOST_CHECK.ps1로 시험용 어셈블리와 로더를 빌드합니다.
ZWCAD에서 output/host-check/HostCheckLoader.dll을 NETLOAD하고 ZWLIBMEMCHECK를 실행합니다.
이 검사는 별도 메모리 DB에서 번호 변경·중복 차단·오래된 정보 차단·역변경·연결 부품 차단을 확인합니다. 활성 작업 도면은 읽거나 수정하지 않습니다.
이는 실제 도면에서의 UNDO 및 저장·재열기 시험을 대신하지 않습니다.
