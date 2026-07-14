# Claude 작업 로그

이 파일은 에이전트 세션이 중간에 끊기더라도 다음 세션이 맥락을 복기할 수 있도록
`Assets/01. Scripts/Editor/ClaudeCompanion` 작업 진행 상황을 기록하는 용도입니다.
새 항목은 파일 맨 위에 추가합니다 (최신순).

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
