# 키 없는 국내·미국 공개 시세 · 2026-11-02

증권사 API 계정·키·토큰 없이 네이버 공개 시세·검색 JSON을 조회합니다. 외부 네트워크는 필요하며, 공개 웹 데이터는 정식 증권사 체결 스트림과 다릅니다. 기존 키움 구현은 레거시 코드로만 보존하고 앱에서는 인증·키 저장·WebSocket 연결을 호출하지 않습니다. 기존에 저장한 키 파일도 자동으로 읽거나 삭제하지 않습니다.

직접 확인한 제공처 주소:

- [종목 검색](https://ac.stock.naver.com/ac?q=%EC%82%BC%EC%84%B1&target=stock&st=111)
- [공개 현재가](https://polling.finance.naver.com/api/realtime/domestic/stock/005930,000660)
- [미국 종목 검색](https://ac.stock.naver.com/ac?q=AAPL&target=stock&st=111)
- [미국 공개 현재가](https://polling.finance.naver.com/api/realtime/worldstock/stock/AAPL.O,SPY,BRKb)
- [미국 시세 지연 정보](https://api.stock.naver.com/stock/AAPL.O/basic)
- [코스피 기본 목록](https://m.stock.naver.com/api/stocks/marketValue/KOSPI?page=1&pageSize=100)
- [네이버 원본 종목 화면](https://m.stock.naver.com/domestic/stock/005930/total)

네이버를 사용하는 이유는 기존 국내 시세와 같은 경로로 국내·미국 검색, 한글 종목명, 주식·ETF를 함께 지원하며 API 키 입력이 필요 없기 때문입니다. [Alpaca 공식 문서](https://docs.alpaca.markets/us/docs/market-data-faq)에 따르면 Alpaca는 인증 키가 필요하고 무료 실시간 피드는 단일 IEX 거래소 데이터이며 통합 SIP 실시간 데이터는 별도 구독이 필요합니다. 이 앱의 키 없는 간단한 사용 방식에는 네이버 경로가 적합합니다. 정식 개발자 계약이나 가용성 보장을 제공하는 API로 간주하지 않습니다.

시작 시 KOSPI/KOSDAQ 각각 상위 100개와 캐시를 기본 목록으로 사용합니다. 입력마다 250ms 대기 후 공개 자동완성을 조회하여 초기 목록 밖의 국내·미국 주식·ETF·영숫자 종목코드도 검색합니다. 이전 입력 요청은 취소하고 현재 입력의 결과만 반영합니다. 미국 종목은 `nationCode=USA`인 검색 응답의 `reutersCode`를 그대로 사용합니다. `AAPL.O`, `SPY`, `BRKb`처럼 식별자가 달라 거래소별 접미사를 임의로 만들지 않습니다. 화면에는 `AAPL`, `SPY`, `BRK B` 같은 티커를 표시합니다.

가격은 열린 팝업의 종목을 한 연결 서비스에서 국내·미국으로 나눈 뒤 최대 20개씩 묶어 조회합니다. 같은 종목을 다시 열거나 초기 조회와 정기 조회가 겹쳐도 캐시와 요청 간격을 공유합니다. 다음 허용 조회 시각은 종목별로 보존하여 미국의 긴 갱신 간격이 국내 갱신을 늦추지 않습니다. 한 시장의 오류가 발생해도 같은 주기의 다른 시장 조회는 처리합니다. 마지막 팝업이 닫히면 양쪽 조회를 중단합니다.

기본 최소 확인 주기는 2초입니다. 응답의 `pollingInterval`이 더 길면 그 간격을 따릅니다. 따라서 화면의 실제 체결 가격이 1~2초마다 갱신된다고 보장할 수 없습니다. 2026-11-02 직접 확인한 미국 장 마감 응답은 `pollingInterval=70000`으로 **70초**였습니다. 애플 기본 정보는 `delayTime=0`, `delayTimeName=실시간`으로 표시됐지만 장중 실제 반영 지연은 이번 장 마감 확인으로 검증하지 못했습니다. 조회 주기와 거래소의 데이터 갱신 지연은 별개입니다.

`closePrice`는 현재가, `fluctuationsRatio`는 전일 대비 등락률입니다. `compareToPreviousPrice.code`가 하락을 뜻하면 비율의 음수 부호를 보정합니다. `localTradedAt`과 `marketStatus`를 보존해 툴팁에 기준 날짜·시각과 장 마감을 표시합니다. 이전 기준 시각의 응답으로 새 가격을 덮어쓰지 않습니다.

미국은 USD 가격을 소수점 최소 두 자리, 최대 네 자리로 표시합니다. `overMarketPriceInfo`에 정규장보다 최신인 `PRE_MARKET`/`AFTER_MARKET` 가격이 있으면 해당 가격을 사용합니다. 프리마켓은 최근 정규장 종가를 전일 기준으로 사용하고, 애프터마켓은 정규장 가격과 전일 대비 변동액에서 전일 종가를 구한 뒤 `(최신 가격 / 전일 종가 - 1) × 100`으로 계산합니다. 장후 응답의 자체 등락률은 당일 정규장 종가 기준이므로 그대로 사용하지 않습니다. 시간·가격·전일 기준이 불완전하면 유효한 정규장 가격을 유지합니다. 주간거래·24시간 거래 전체를 지원한다고 보장하지 않습니다.

HTTP 429는 Retry-After 동안 추가 조회를 막습니다. 그 밖의 연결 오류는 재시도 간격을 최대 30초까지 늘립니다. 시세 형식 변경·응답 누락은 오류로 표시하고 0원이나 예시 가격으로 대체하지 않습니다. 공개 경로는 제공처에서 변경·제한할 수 있습니다.
