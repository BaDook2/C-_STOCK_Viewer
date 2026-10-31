> 레거시 문서: 2026-10-31부터 앱은 키 없는 공개 시세를 사용합니다. 아래 키움 구현은 실행 경로에서 사용하지 않습니다. 현재 동작은 PublicData.md를 참고하세요.

# 키움 REST API 연결

공식 참고: [키움 REST API](https://openapi.kiwoom.com/), [키움 공식 샘플](https://github.com/Kiwoom-Securities/Kiwoom-REST-API).

| 용도 | 요청 |
|---|---|
| 인증 | `POST /oauth2/token`, `au10001`, client_credentials, appkey, secretkey |
| 종목 목록 | `POST /api/dostk/stkinfo`, `ka10099`, mrkt_tp = 0/10/8/50 |
| 초기 현재가 | `POST /api/dostk/stkinfo`, `ka10001`, stk_cd |
| 실시간 | `wss://api.kiwoom.com:10000/api/dostk/websocket`, `0B` |

모의 환경은 `mockapi.kiwoom.com`을 사용합니다. 키/토큰을 로그에 쓰거나 STOCK의 기존 저장 키를 읽지 않습니다.

토큰 유효기간은 KST `expires_dt`를 해석합니다. 만료 전에 인증과 소켓 연결을 갱신합니다. HTTP 요청은 직렬화하고 실서버 650ms, 모의서버 1100ms 최소 간격을 둡니다. 이는 앱 자체의 보수적 제한이며 다른 프로그램의 같은 계정 API 사용까지 조절하지 않습니다.

목록은 `cont-yn`, `next-key`로 연속 조회합니다. 누락·반복 키와 최대 페이지 초과를 오류로 처리하고 중복 종목 코드를 합칩니다. 신규 영숫자 종목 코드도 지원합니다.

WebSocket은 LOGIN 성공 확인 후 REG를 보냅니다. REG는 그룹 1의 전체 현재 팝업 목록을 `refresh=0`으로 동기화합니다. 마지막 팝업이 닫히면 REMOVE를 보냅니다. PING은 수신 내용 그대로 응답합니다. 체결 값 10=현재가, 12=전일 대비 등락률, 20=체결시각을 사용하고 현재가 부호는 절댓값으로 변환합니다.

연결·인증·응답 오류 또는 55초 이상 수신 없음은 상태에 반영하고 최대 30초까지 증가하는 간격으로 재접속합니다. 재접속 후 현재 구독을 복원합니다. 시세 조회에 실패하면 실패를 표시하고 예시 시세로 바꾸지 않습니다. 아직 실제 키·실시간 장중 수신을 검증하지 않았습니다.
