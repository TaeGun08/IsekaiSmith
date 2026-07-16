# Claude 작업 로그

이 파일은 에이전트 세션이 중간에 끊기더라도 다음 세션이 맥락을 복기할 수 있도록
`Assets/01. Scripts/Editor/ClaudeCompanion` 작업 진행 상황을 기록하는 용도입니다.
새 항목은 파일 맨 위에 추가합니다 (최신순).

---

## 2026-07-16 리뉴얼 착수 — UI Toolkit 전환 결정 + 셸 마이그레이션(M1) 완료

사용자 요청: "차라리 너가 계속 못 고치니깐 리뉴얼을 하자" — 반복된 레이아웃 붕괴 버그(입력창
사라짐, 채팅 높이 깨짐 등, 이 로그의 2026-07-14 항목들 참고)를 근본적으로 없애고, 디자인/
UI·UX를 "AI 개발을 시각적으로 즐길 수 있게" 다시 설계하기로 함.

### 기획 (M0)
- 반복 버그의 근본 원인은 IMGUI의 수동 rect/높이 계산이라고 진단 → **UI Toolkit(UXML/USS)로
  전환**하기로 결정. Flexbox 기반이라 이 버그 클래스가 구조적으로 사라짐.
- 캐릭터는 스프라이트 아트(B) 대신 **절차적 벡터(A)** 유지·확장 — 코드로 계속 다듬기 쉽고
  상태별 일관성 유지가 쉬움.
- 마일스톤: M0 설계 → M1 셸 마이그레이션(기능 동등성) → M2 비주얼 아이덴티티 → M3 캐릭터
  상태 확장 → M4 턴 진행 스텝퍼(로그 부활) → M5 마이크로인터랙션 → M6 안정화.
- 데이터/로직 경계 확정: `CompanionSession`/`ChatMessage`/`ChatMarkdown`/`CompanionLog`/
  `ClaudeSessionRunner`는 이미 UI 프레임워크에 무관한 순수 C#이라 **전혀 손대지 않음** —
  `ClaudeCompanionWindow`의 View 레이어만 교체.

### M1 구현 (같은 날)
- `Assets/01. Scripts/Editor/ClaudeCompanion/UI/ClaudeCompanionStyles.uss` 신규 — 기존
  IMGUI 팔레트와 동일한 색으로 정적 스타일만 정의 (비주얼 변경은 M2로 미룸).
- `UI/CharacterStageElement.cs` 신규 — 원형 텍스처(`CreateCircleTexture`) 대신
  `VisualElement`(border-radius 50%) 조합으로 캐릭터 재구성. bob/blink/busy 오빗닷 로직은
  기존 수식 그대로 이식, `Tick(bool busy, double t)`로 매 프레임 좌표만 갱신 (내부에서
  MarkDirtyRepaint 안 부름 — 호스트 창의 스케줄러가 담당).
- `ClaudeCompanionWindow.cs` 전면 재작성: `OnGUI()` → `CreateGUI()`. 세션 매니페스트
  저장/복원, `OnEnable`/`OnDisable` 로직은 그대로 유지. `GetChatPaneWidth`/
  `CalculateChatScrollHeight` 같은 수동 폭·높이 계산 전부 삭제 (Flexbox가 대체).
  Enter-전송/Shift+Enter-줄바꿈은 IMGUI의 `Event.Use()` 트릭 대신 `TrickleDown.TrickleDown`
  단계에서 `KeyDownEvent`를 가로채는 방식으로 교체. `EditorApplication.update` 기반
  `RepaintInterval` 스로틀 제거 → UI Toolkit `root.schedule.Execute(...).Every(16)`으로
  대체 (애니메이션 60fps, 브릿지 상태 폴링은 500ms 별도 스케줄).
- `ClaudeCompanionSendDialog.cs`(대체 입력창 IMGUI 폴백)는 그대로 유지 — 새 입력창이
  한동안 안정적으로 검증된 뒤 제거 후보.

### 검증 상태 (미완료 — 다음 세션에서 이어서 할 것)
`refresh_unity` 컴파일 요청이 60초 타임아웃, `read_console`에도 아직 에러가 안 찍힘 — 이
대화 세션 자체가 `ClaudeCompanionWindow`가 띄운 `ClaudeSessionRunner` 프로세스일 가능성이
높아 `LockReloadAssemblies`가 이 세션이 끝날 때까지 컴파일을 미루고 있는 것으로 추정
(2026-07-15 `claude-companion-parallel-sessions` 메모에 기록된 것과 같은 패턴).

### 다음에 할 일 (TODO)
- [ ] 다음 세션 시작 시 `read_console`로 컴파일 에러 유무 먼저 확인
- [ ] 에러 있으면 수정, 없으면 Unity 에디터에서 실제로 창 열어서 레이아웃 확인
      (세션 전환/추가/삭제, 창 크기 조절, 메시지 전송/큐잉/취소, 코드블록·마크다운 렌더링,
      브릿지 Start/Stop 전부 육안 회귀 확인 — 아직 전혀 미검증)
- [ ] M1 안정성 확인되면 사용자에게 보여주고 M2(비주얼 아이덴티티)로 진행할지 확인
- [ ] Task #2 (M1 마이그레이션) 완료 처리는 위 검증 끝난 뒤에

---

## 2026-07-14 (4) 로그 접기/리사이즈 시 채팅 영역이 실제로 넓어지지 않던 문제 수정

사용자 보고: "현재 로그 창만 넓어지고 접히고 하는데 내가 원하는건, 그 작용을 통해서 채팅창이
넓어지는 효과를 기대한 거야." 기존 구현은 채팅 스크롤뷰를 `GUILayout.ExpandHeight(true)`로
"남는 공간 자동 차지" 방식으로 만들면 로그를 줄이거나 접었을 때 자동으로 채팅이 커질 거라
가정했으나, 채팅 버블과 로그 항목 둘 다 줄바꿈되는 가변 높이 콘텐츠라 IMGUI의 expand 계산이
안정적으로 반응하지 않음 (스크롤바 유무 → 줄바꿈 폭 → expand 결과가 서로 영향을 주는 순환).

`ClaudeCompanionWindow.cs` 수정:
- `CalculateChatScrollHeight()` 추가: 매 프레임 "창 높이 − 위쪽 고정 UI(GetLastRect로 실측) −
  로그 영역이 실제로 차지하는 높이(activityLogCollapsed/activityLogHeight로 직접 계산, 우리가
  전부 통제하는 값이라 정확함) − 구분선"을 계산해 채팅 스크롤뷰에 고정 높이로 전달.
- `DrawChat()`이 `GUILayout.ExpandHeight(true)+MinHeight(220)` 대신 이 계산된 높이를
  `GUILayout.Height(...)`로 받도록 변경.
- 로그 핸들 높이(6f)를 `ActivityLogHandleHeight` 상수로, 구분선 높이(13f)를
  `DividerTotalHeight` 상수로 추출해 계산식과 실제 그리기 코드가 어긋나지 않도록 함.

검증: `mcp__UnityMCP__refresh_unity` 컴파일 요청 후 `read_console` 에러/경고 0건.

### 다음에 할 일 (TODO)
- [ ] Unity 에디터에서 실제로 로그 접기/드래그 시 채팅 영역이 육안으로 커지는지 확인 (아직 미검증)

---

## 2026-07-14 (3) 도메인 리로드 후 브릿지 UI가 계속 "중지됨"으로 멈추는 문제 수정

사용자 보고: "UI작업이 끝나고 나서, 너의 활동이 정지가 되는 상태를 없애줬으면 해. 계속 꺼지면 켜줘야 하잖아."
Claude가 스크립트를 수정할 때마다 Unity가 재컴파일(도메인 리로드)되는데, MCP 브릿지 패키지 자체는
`HttpBridgeReloadHandler`로 백그라운드에서 자동 재연결하지만, `ClaudeCompanionWindow`의
`bridgeRunning` 필드는 `OnEnable`/Start·Stop 버튼 클릭 시에만 갱신되어 실제로는 재연결이 끝났는데도
UI가 "브릿지 중지됨"으로 계속 멈춰 있었음. 그 결과 사용자가 매번 Start를 다시 눌러야 했고, 그 버튼은
무조건 `runner.ResetSession()` + 채팅/로그 초기화를 실행해 대화 맥락까지 날려버리는 부작용이 있었음.

`ClaudeCompanionWindow.cs` 수정:
- `OnGUI()` 매 프레임 `bridgeRunning = MCPServiceLocator.Bridge.IsRunning`으로 실제 상태와 동기화
  (예외는 조용히 무시 — 리로드 직후 서비스가 아직 준비 안 됐을 수 있어 매 프레임 로그 스팸 방지).
- `StartSession()`은 `restoredSessionId`가 이미 있을 때(=재연결 상황)는 세션/채팅을 초기화하지 않도록 변경.
  진짜 새 세션을 시작할 때만 리셋.

검증: `mcp__UnityMCP__refresh_unity` 컴파일 요청 후 `read_console` 에러/경고 0건.

---

## 2026-07-14 (2) 채팅/로그 영역 리사이즈 + 로그 접기 기능 추가

사용자 요청: "채팅 창을 넓힐 수 있는 기능"과 "도구 활동 로그 창을 열고 닫을 수 있는 기능".
`ClaudeCompanionWindow.cs`에 다음 추가 (아직 커밋 안 됨, 위 항목의 미커밋 변경 위에 얹은 것):

- `activityLogCollapsed` (bool), `activityLogHeight` (float, 기본 100) 필드를 `[SerializeField]`로 추가
  — 도메인 리로드 후에도 사용자가 조절한 상태 유지 (restoredSessionId와 같은 패턴).
- `DrawActivityLog()` 헤더에 "접기 ▼ / 펼치기 ▲" 버튼 추가. 접으면 스크롤뷰·리사이즈 핸들·로그 경로
  라벨을 그리지 않고 헤더 줄만 남김.
- `DrawActivityLogResizeHandle()` 추가: 채팅과 로그 사이에 6px 드래그 핸들(스플리터).
  `MouseCursor.ResizeVertical` 커서, 드래그 시 `activityLogHeight`를 `MinActivityLogHeight`(60)~
  `position.height * MaxActivityLogHeightRatio`(0.6) 범위로 clamp.
  로그 영역이 줄어들면(또는 접히면) 채팅 영역은 `GUILayout.ExpandHeight(true)` 덕분에 자동으로
  넓어짐 — 별도의 채팅 높이 계산 로직 불필요.

검증: `mcp__UnityMCP__refresh_unity` 후 `read_console` 에러/경고 0건.

### 다음에 할 일 (TODO)
- [ ] Unity 에디터에서 실제로 드래그/접기 동작 육안 확인 (MCP로는 컴파일만 확인, 실제 클릭/드래그 상호작용은 미검증)
- [ ] 위 변경사항 전부(세션 영속화 + 리사이즈/접기) 커밋 — 사용자 확인 후 진행
- [ ] 추가 기능 방향은 사용자 지시 대기

---

## 2026-07-14

### 현재 상태 파악 (커밋 안 된 변경사항 분석)
`ClaudeCompanionWindow.cs`, `ClaudeSessionRunner.cs`에 아직 커밋되지 않은 변경 발견. 내용 확인 결과:

- **세션 영속화**: `restoredSessionId`를 `[SerializeField]`로 저장해 도메인 리로드(스크립트 재컴파일) 후에도
  `ClaudeSessionRunner.RestoreSession()`으로 같은 Claude 세션에 이어 붙도록 함.
  (이전에는 화면상 채팅은 이어져 보여도 실제 세션은 몰래 새로 시작되는 버그가 있었음)
- **토큰 사용량 배지 제거**: `TokenUsage` 구조체, `OnUsageUpdated` 이벤트, `DrawTokenUsageBadge()`,
  `FormatTokenCount()`, `tokenBudget`/`EditorPrefs` 저장 로직 전부 삭제. (grep으로 잔여 참조 없음 확인)
- **permission-mode 고정**: `autoProceed` 옵션 제거하고 항상 `bypassPermissions` 사용.
  이유(코드 주석): headless(-p, stdin 미연결) 프로세스라 인터랙티브 권한 프롬프트에 응답할 방법이 없고,
  `acceptEdits`는 파일 편집 외의 도구 호출(Bash, UnityMCP manage_* 등)을 멈춰 세워버림.
- **유휴 타임아웃 추가**: 10분간 출력이 없으면 프로세스를 강제 종료 (`LockReloadAssemblies`가 무한정
  걸려 있는 것 방지).
- **OnGUI 방어 처리**: try/catch로 감싸 한 프레임 예외로 창 전체가 죽지 않게 함.
- **창 최소 크기**: 420×680 → 480×760.

검증: `CompanionLog.cs`, `ChatMessage.cs`에는 제거된 필드/이벤트에 대한 잔여 참조 없음 (grep 확인).
Unity 콘솔(`read_console`) 확인 결과 에러/경고 0건 — 컴파일 정상.

**결론**: 이 변경 세트는 자체적으로 일관되고 완결된 상태. 다음 단계로 커밋 예정.

### 다음에 할 일 (TODO)
- [ ] 위 변경사항 커밋
- [ ] 사용자에게 다음 작업 방향 확인 필요 (구체적 다음 기능 지시 없음)
