Ummate UI Ver.2

FarmScene에는 연결을 완료했습니다. Unity Editor 조작 / Play Mode / Editor Scene 저장은 하지 않았습니다.
원본 UI PNG와 .meta는 보존하며 PreparedSprites에는 적용용 복사본만 있습니다.

MissionUI: 닫아도 MissionProgress는 계속 갱신합니다. 다시 열면 최신 텍스트가 표시됩니다.
MissionReopenButton/Icon: Image > Source Image에 최종 아이콘을 넣으면 실행 중 임시 '미션' 글자가 자동으로 숨겨집니다.
DayPanel/WeatherIcon: 빈 원형 임시 Sprite입니다. Source Image만 교체하세요. 날씨 로직은 없습니다.
Quickbar/QuickbarIcons.asset: Item과 Sprite를 대응시키는 UI 전용 목록입니다. 최종 아이템 그림이 없어 현재는 이름으로 표시합니다. 말풍선용 이미지와는 분리했습니다.
아이템 아이콘을 추가할 때 ItemDefinition, Inventory 또는 PlayerQuickbar를 수정할 필요가 없습니다.
MissionUI.prefab은 재사용용 UI 템플릿입니다. 현재 FarmScene의 MissionLabel 참조는 보존했습니다.

참고 이미지에서 토마토, 발바닥, 새싹, 설정 톱니 및 F 쓰다듬기는 표현 참고이며 게임 기능/아이템을 추가하지 않았습니다.
Scene을 디스크에서 다시 열고 미션 X/재열기, Tip X/재열기 및 DEV, 날짜/문 근처 안내, 숫자키/휠 선택과 수량을 확인하세요.
