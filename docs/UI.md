# 화면 동작

검색 창은 286×80 DIP로, 종목이름 Display와 톱니바퀴·최소화·닫기를 첫 줄에, 검색 입력·버튼을 다음 줄에 표시합니다. 다른 정보나 상태줄은 없습니다.

| UI | 기능 | 코드 |
|---|---|---|
| 종목이름 Display | 앞으로 새로 여는 팝업의 이름 표시 여부를 결정. 기존 창은 유지 | DisplayNameChanged, OpenStockAsync |
| 검색 입력 | 로컬 초성·코드 검색과 250ms 디바운스 공개 자동완성 | RefreshSuggestions, CompleteSuggestionsAsync |
| 검색/Enter/결과 클릭 | 현재 체크박스 값으로 종목 팝업 생성. 동일 코드는 교체 | OpenSelectionAsync, OpenStockAsync |
| 톱니바퀴 | 공개 시세 상태·주기 안내·다시 연결. API 키 입력 없음 | SettingsWindow |
| 가격 창 우클릭 1회 | Fix/Free를 즉시 전환 | PopupRightClick, ProcessRightClick |
| 가격 창 우클릭 2회 | 해당 창을 닫고 구독 제거 | PopupRightClick, ProcessRightClick |
| Esc | 가격 창 닫기 또는 검색 자동완성 닫기 | PopupKeyDown, SearchKeyDown |
| 검색 창 최소화 | 검색 창만 최소화 | Minimize |
| 검색 창 X | 검색 창만 닫고 가격 창과 수신은 유지. 가격 창이 없으면 프로그램 종료 | OnClosed, EndSessionAsync |

팝업은 높이 32 DIP에 이름·가격·등락률 한 줄입니다. 이름 숨김은 창별로 독립적이고 너비도 줄입니다. 코드·시장·상태줄·버튼·우클릭 메뉴는 표시하지 않습니다. 우클릭 단일/더블 판정은 WPF의 시스템 더블클릭 간격을 따릅니다.

새 가격 팝업은 검색 입력칸을 가리지 않도록 검색 창 아래 4 DIP 간격에 가로 중앙을 맞춰 표시합니다. 화면 아래쪽 공간이 부족하면 검색 창 위에 표시하고 현재 모니터의 작업 영역 안으로 제한합니다. 검색 창을 닫아도 가격 갱신은 계속되며, 마지막 가격 창을 닫을 때 시세 연결을 정리하고 프로그램을 종료합니다.

툴팁에 공개 시세 제공처·기준 날짜와 시각·장 마감·지연·Fix/Free 상태를 표시합니다. 단절 시 마지막 가격을 흐리게 표시합니다. 상승 빨강, 하락 파랑, 보합 회색은 기존 STOCK 색상과 같습니다.
