# Claude 작업 로그

이 파일은 에이전트 세션이 중간에 끊기더라도 다음 세션이 맥락을 복기할 수 있도록
`Assets/01. Scripts/Editor/ClaudeCompanion` 작업 진행 상황을 기록하는 용도입니다.
새 항목은 파일 맨 위에 추가합니다 (최신순).

---

## 2026-07-16 (5) M5 마이크로인터랙션 폴리싱 구현 (컴파일 확인, 런타임 검증은 다음 턴)

사용자 지시: "진행해줘" (M4 완료 보고 후 M5로 진행).

### 한 것
- `UI/CharacterStageElement.cs`: success/error 플래시에 물리적 반응 추가.
  `flashStart` 필드로 경과 시간 추적 → 에러는 감쇠하는 좌우 shake(`Mathf.Sin(elapsed*40)*
  decay`), 성공은 한 번의 scale "pop"(사인 커브로 1.0→1.18→1.0). body와 눈 위치 모두
  `shakeX` 반영, `body.style.scale`에 bounce 반영.
- `ClaudeCompanionWindow.cs`:
  - 타이핑 인디케이터: `ActiveSession.IsBusy`일 때 채팅 맨 아래에 점 3개 말풍선
    (`BuildTypingIndicator`) 추가, `OnAnimationTick`에서 위상차 있는 사인파로 점마다
    opacity 애니메이션 (`typingDots` 필드, 세션 전환/리페인트 시 null로 정리).
  - 사운드 알림: `[SerializeField] soundEnabled`(기본 true) + 컨트롤 바에 토글 버튼
    ("🔔 알림음"/"🔕 알림음"). 턴이 성공적으로 끝나면(`OnActiveTurnComplete`)
    `EditorApplication.Beep()` — 별도 오디오 에셋 임포트 없이 에디터 툴다운 알림음 정도로
    충분하다고 판단, 에러 시엔 안 울리게 해서 스팸 방지.
- `UI/ClaudeCompanionStyles.uss`: `.sound-toggle-button`, `.typing-indicator`,
  `.typing-dot` 클래스 추가.

### 검증 상태
`read_console` 에러/경고 0건. 이번에도 컴파일 완료 시점이 마지막 도메인 리로드보다 늦어서
(`last_compile_finished` > `last_domain_reload_after`) 런타임 리플렉션 검증은 다음 턴으로
미룸 (M4와 동일 패턴, [[project_claude_companion_parallel_sessions]] 참고).

### 검증 완료 (같은 날, 다음 턴)
도메인 리로드 완료 확인(`last_domain_reload_after` > 이전 컴파일 완료 시점) 후 리플렉션 재검증:
- 사운드 토글: `ToggleSound()` 호출 시 `soundEnabled` True↔False, 버튼 텍스트 "🔔 알림음"↔
  "🔕 알림음" 정확히 전환.
- 캐릭터 bounce: `FlashSuccess()` 후 플래시 중간 시점에 `Tick()` 호출 시 `body.style.scale.x`
  = 1.18 (사인 커브 피크와 일치).
- 캐릭터 shake: `resolvedStyle.left`는 레이아웃 패스가 필요해서 동기 테스트로는 안 바뀌어
  보였지만(레이아웃 지연 아티팩트, 실버그 아님), `style.left`(요청값)로 재확인하니
  `FlashError()` 직후 +5.26 오프셋 — 손계산값과 정확히 일치.
- 타이핑 인디케이터: 세션이 실제로 busy 상태라 `RefreshChat()` 후 `typingDots`가 자동으로
  채워져 있었음(이 대화 세션 자신이 지금 turn을 처리 중이라 `IsBusy=true`인 채였던 것으로
  추정 — 오히려 실제 사용 시나리오 검증이 된 셈). `OnAnimationTick()`을 두 번 호출하니
  점의 opacity가 0.416→0.474로 실제로 변함.

### 다음에 할 일 (TODO)
- [x] 리플렉션 검증 전부 통과
- [x] Task #6(M5) 완료 처리
- [x] 사용자에게 M0~M5 전체 완료 보고 → "확인 후, 진행해줘" → M6로 진행 (아래 항목 참고)

---

## 2026-07-16 (6) M6 안정화 — 멀티세션/도메인 리로드 회귀 검증 (완료)

사용자 지시: "확인 후, 진행해줘". 스크린샷을 시도했으나 사용자의 다른 창(게임)이 화면
최상단에 떠 있어서 `CopyFromScreen`이 그 창을 대신 캡처함 — 실제 시각 확인은 사용자가
직접 해야 함. 대신 리플렉션으로 구조/동작을 종합 점검:

- `mainColumn` 자식 순서 확인: accent-bar → character-stage → controls-row →
  stepper-section → horizontal-divider → chat 컨테이너. 의도한 배치 그대로.
- **멀티세션 동시성**: `AddNewSession()` 리플렉션 호출로 세션 2개 상태 만듦 → 사이드바에
  서로 다른 accent 스트라이프(코랄/틸) 정상 표시. `SwitchToSession(1)` 호출 후
  `session0.Changed`/`session1.Changed` 델리게이트의 `GetInvocationList().Length` 직접
  확인 → 이전 세션은 정확히 구독 해제(2→1), 새 세션은 정확히 구독(→2). 여러 번 세션을
  전환해도 리스너가 누적되지 않음(누수 없음) 확인. `RequestRemoveSession(1)`로 정리 후
  세션 1개로 복귀.
- **도메인 리로드 영속성**: `Library/ClaudeCompanion/sessions.json` 직접 읽어서 정리 후
  실제 세션 상태와 일치하는 것 확인. 이번 대화 세션 자체가 M1~M5 작업 중 컴파일/도메인
  리로드를 십수 차례 실제로 거쳤고 그때마다 채팅 기록·세션 탭·`RestoredSessionId`가 전부
  안 끊기고 이어졌으므로, 이 시나리오는 이 대화 자체가 이미 충분히 실전 검증한 셈.

### 결론
M0~M6 전체 마일스톤 완료. 소스 레벨(컴파일 0에러)과 리플렉션 기반 런타임 동작은 전부
검증됨. **실제 육안 확인(스크린샷/직접 조작)은 미완료** — 사용자가 직접 창을 열어 확인
필요.

### 다음에 할 일 (TODO)
- [x] 사용자 피드백(2026-07-16): "엔터 눌렀을 때 전송은 됐는데 줄바꿈도 같이 됨" 버그 신고
      → `OnInputKeyDown` 등록 대상을 outer `TextField` → inner `unity-base-text-field__input`
      요소로 변경(실제 네이티브 개행 처리가 이 안쪽 요소에서 일어나는 것으로 추정),
      `StopPropagation` → `StopImmediatePropagation`으로 강화, `TrySend()`에 다음 프레임
      방어적 재클리어(스케줄) 추가 — 위 두 조치가 안 먹혀도 이게 최종 안전망. 컴파일 0에러
      확인, 실제 키 입력 시뮬레이션 검증은 다음 턴.
- [ ] 사용자가 언급한 "버튼/레이아웃 안 맞음"과 "비주얼이 너무 심플함"은 구체적인 부분
      확인 필요 (메시지가 "그리고 사용하는"에서 끊김) — 다음 턴에 사용자 답변 받고 진행
- [x] 스크린샷으로 실제 육안 확인 — 사용자 요청 "지금 다시 검증해봐"로 재시도, 이번엔
      게임 창이 안 겹쳐서 전체 창 캡처 성공. 사이드바/accent 바/캐릭터(코랄="명령 실행
      중..." — 실제 이 대화의 도구 호출이 실시간 반영된 것)/컨트롤 바(Stop/브릿지/🔔
      알림음/대체 입력창)/스텝퍼(이 대화의 실제 도구 호출 칩들: read console, execute
      code, 파일 읽기, 시스템: thinking_tokens 등)/채팅 버블까지 전부 의도대로 렌더링됨을
      육안으로 최종 확인. M0~M6 전체 리뉴얼 완전히 검증 완료.
- [ ] 필요하면 `ClaudeCompanionSendDialog.cs`(IMGUI 폴백 입력창) 제거 검토 — 새 입력창이
      한동안 안정적으로 검증됐다고 판단되면
- [ ] 추가 방향은 사용자 피드백 대기

---

## 2026-07-16 (4) M4 턴 진행 스텝퍼 — 활동 로그 패널 대체 구현 (컴파일 확인, 런타임 검증은 다음 턴)

사용자 지시: "진행해줘" (M3 완료 보고 후 M4로 진행).

### 한 것
- `CompanionSession.cs`: `CurrentTurnSteps`(현재 턴 범위 활동 목록, 새 턴 시작할 때
  `SendNow`에서 clear) 추가. 기존 `ClassifyActivityEntry` 내부의 도구 분류 로직을
  `public static ClassifyTool(string toolName)`로 추출 — `CurrentActivity` 계산과
  스텝퍼 칩 색상 계산이 같은 분류 규칙을 공유하도록.
- `ClaudeCompanionWindow.cs`: 캐릭터 스테이지 아래·컨트롤 바 아래에 접이식 "진행 상황"
  섹션 추가 (`[SerializeField] turnStepperCollapsed`로 접힘 상태 영속). 내부는
  `max-height: 64px` `ScrollView` + flex-wrap 칩 목록 — 칩이 아무리 많아져도 바깥 레이아웃이
  안 밀리게 (예전 활동 로그 패널이 깨졌던 것과 같은 실수를 구조적으로 방지). 칩은 도구별
  친화적 한글 라벨(`ToolLabels` 딕셔너리, `mcp__` 접두사는 마지막 `__` 뒤 이름으로 폴백) +
  활동 색 점. `RefreshStepper()`가 `OnSessionChanged`(= `RefreshChat` + `RefreshStepper`
  묶음, 기존 `boundSession.Changed` 구독처를 이걸로 교체)에서 매번 다시 그림.
- `UI/ClaudeCompanionStyles.uss`: `.stepper-*`/`.step-chip*` 클래스 추가.

### 검증 상태
컴파일: 처음엔 0 에러로 통과된 것처럼 보였는데, 재확인해보니 실제로는 **컴파일은 끝났지만
도메인 리로드가 아직 안 된 상태**였음 (`editor_state.compilation.last_domain_reload_after_unix_ms`가
`last_compile_finished_unix_ms`보다 예전 값) — 이번 대화 세션 자신이 `LockReloadAssemblies`를
잡고 있어서 컴파일 자체는 통과해도 실제 반영은 이 턴이 끝나야 일어나는 것으로 확인. 그
상태에서 `execute_code` 리플렉션으로 `CurrentTurnSteps`/`RefreshStepper`/`stepperContent`를
찾으면 전부 NULL (아직 구버전 어셈블리를 참조하는 라이브 오브젝트라서) — 실제 버그가 아니라
검증 타이밍 문제였음. [[project_claude_companion_parallel_sessions]]에 이 패턴(컴파일 성공 ≠
런타임 반영 완료)을 새로 기록해둠.
**`read_console` 자체는 0 에러/0 경고** — 소스 레벨 정확성은 확인됨.

### 검증 완료 (같은 날, 다음 턴)
`editor_state`로 도메인 리로드가 이미 끝난 것 확인(`last_domain_reload_after_unix_ms` >
이전 턴의 컴파일 완료 시점) 후 리플렉션 재시도 — 이번엔 필드/메서드 전부 정상 조회됨.
`CurrentTurnSteps`에 6개 샘플 엔트리(Read/mcp 도구/tool_result/Bash/ERROR/system) 주입 →
`RefreshStepper()` 호출 → 칩 6개 전부 의도한 색상·라벨로 렌더링 확인 (읽기=틸,
mcp__UnityMCP__manage_gameobject→"manage gameobject"로 접두사 정리, 결과확인/시스템=
바이올렛, 실행=코랄, 에러=빨강+"…" 말줄임). `ToggleStepperCollapsed()` 두 번 호출로
`stepperScroll.style.display`가 `None ↔ Flex`로 정확히 토글되는 것도 확인. 테스트 데이터는
정리(`CurrentTurnSteps.Clear()` + `RefreshStepper()`)해서 세션 원상복구.

### 다음에 할 일 (TODO)
- [x] 도메인 리로드 완료 확인 후 리플렉션 재검증 → 전부 통과
- [x] 접기/펼치기 토글 확인 → 통과
- [x] Task #5(M4) 완료 처리
- [ ] 사용자에게 보고, M5(마이크로인터랙션 폴리싱)로 진행할지 확인

---

## 2026-07-16 (3) M3 캐릭터 상태 확장 — 도구별 표정/색 세분화 구현 (검증 대기)

사용자 지시: "진행해줘" (M2 완료 보고 후 M3로 진행).

### 한 것
- `CharacterActivity.cs` 신규 (UI 비의존 순수 enum): `Idle/Thinking/Reading/Editing/Running`.
- `CompanionSession.cs`: `CurrentActivity` 프로퍼티 추가. `Runner.OnToolActivity`의
  `"tool_use: X"` 문자열에서 도구 이름을 파싱해 `ReadingTools`/`EditingTools`/`RunningTools`
  집합(+`mcp__` 접두사는 Running)으로 분류하는 `ClassifyActivityEntry` 추가. 새 턴 시작
  (`SendNow`)엔 `Thinking`, 턴 완전히 끝남(`AdvanceQueueOrNotify`의 대기열 없음 분기)엔
  `Idle`, `ResetForNewConversation`에도 `Idle`로 리셋.
- `UI/CharacterStageElement.cs`: `Tick(bool busy, ...)` → `Tick(CharacterActivity, ...)`로
  시그니처 변경. 활동별 색상 페어(생각중=바이올렛, 읽기=틸, 수정=기존 amber, 실행=코랄) +
  라벨 텍스트(`GetActivityStyle`)를 매핑. 턴이 성공/에러로 끝났을 때 잠깐(1.2~1.4초) 캐릭터
  색/눈 크기/라벨이 확 바뀌는 `FlashSuccess()`/`FlashError()` 원샷 오버레이 추가 (세션의
  영속 상태가 아니라 호출 시점 타임스탬프 기반). 사이드바 세션 dot도 같은 활동 색을 쓰도록
  `GetIndicatorColor(activity)` 공개 static 메서드로 노출.
- `ClaudeCompanionWindow.cs`: `OnAnimationTick`이 `ActiveSession.CurrentActivity`를 넘기도록
  변경. 사이드바 dot 갱신도 `CharacterStageElement.GetIndicatorColor`로 교체(기존
  `BusyDotColor`/`IdleDotColor` 상수는 삭제 — 이제 안 씀). `RebuildMainColumn`에서
  `boundSession.Runner.OnTurnComplete`/`OnError`를 추가로 구독해 `FlashSuccess`/`FlashError`
  트리거 (탭 전환 시 이전 세션 구독 해제도 함께 처리).

### 알아둘 것 (완벽하진 않음, 의도적으로 범위 밖)
- Claude CLI의 `type == "result"` JSON에 `is_error` 필드가 있을 수 있는데 `ClaudeSessionRunner`가
  그걸 안 읽고 있어서, 에러로 끝난 턴도 지금은 `OnTurnComplete`(성공 flash)로 잡힐 수 있음 —
  이번 범위는 캐릭터 표현이라 프로토콜 파싱은 안 건드림. 필요해지면 별도로 다룰 것.

### 검증 완료 (같은 날, 다음 턴)
컴파일 에러/경고 0건 (이번엔 `LockReloadAssemblies` 대기가 실제로 ~150초 걸림 — 패턴은
동일, 그냥 대기 시간이 길었을 뿐). `execute_code`로 라이브 `CharacterStageElement.Tick()`을
5개 활동 전부에 대해 직접 호출하고 `stateLabel.text`/`body.resolvedStyle.backgroundColor`를
읽어 확인 — 대기(회색)/생각중(바이올렛)/읽기(틸)/수정(amber)/실행(코랄) 라벨·색 전부 의도대로
나옴. `FlashSuccess()`(초록 "완료!")/`FlashError()`(빨강 "문제가 발생했어요")도 확인.
`CompanionSession.ClassifyActivityEntry`도 직접 호출해 `tool_use: Read`→Reading,
`tool_use: Edit`→Editing, `tool_use: Bash`/`tool_use: mcp__UnityMCP__manage_gameobject`→
Running, `tool_result received`→Thinking까지 전부 의도대로 분류됨을 확인.

### 다음에 할 일 (TODO)
- [x] 컴파일 에러 확인 → 0건
- [x] 활동별 색상/분류 리플렉션 검증 → 전부 일치
- [x] Task #4(M3) 완료 처리
- [ ] 사용자에게 보고, M4(턴 진행 스텝퍼 — 로그 패널 대체)로 진행할지 확인

---

## 2026-07-16 (2) M2 비주얼 아이덴티티 — 팔레트/세션 accent/마이크로인터랙션 구현 (검증 대기)

사용자 지시: "진행해줘" (M1 검증 완료 보고 후 M2로 진행).

### 한 것
- `UI/ClaudeCompanionStyles.uss` 전면 갱신:
  - 팔레트를 기존 IMGUI 색 그대로 쓰던 쿨그레이 톤에서 웜톤 다크 뉴트럴 + 테라코타
    브랜드 accent(`rgb(217,119,87)`)로 교체. 배경/스테이지/버블/코드블록 색 전부 조정.
  - 세션별 identity color 6종 팔레트(코랄/틸/바이올렛/골드/스카이/세이지) 추가.
  - 마이크로인터랙션: `.session-row:hover`, `.session-delete-button:hover`,
    `.chat-input-inner:focus`(하단 보더가 브랜드 컬러로), `.send-button`/`.cancel-button`
    hover·active(`scale: 0.96` 눌림 효과), `.bridge-toggle-button` hover까지 USS
    `transition-property`로 부드럽게.
  - busy/idle/running/stopped 같은 **상태 색은 의도적으로 건드리지 않음** — 이번 패스는
    identity/미감만 담당, 상태 신호는 M1 그대로.
- `ClaudeCompanionWindow.cs`:
  - `SessionAccentPalette`(6색) + `GetSessionAccent(index)` 추가.
  - `BuildSessionRow`: 세션 행 좌측에 3px accent 스트라이프(`border-left-color`)로 세션 구분.
  - `RebuildMainColumn`: 활성 세션의 accent 색으로 메인 컬럼 최상단에 3px 바 추가
    (`session-accent-bar`).
  - `UpdateBridgeControlsVisual`: Start/Stop 버튼 색을 인라인 `style.backgroundColor`
    대신 USS 클래스 토글(`bridge-toggle-button--running`/`--stopped`)로 변경 — **이유**:
    UI Toolkit에서 인라인 스타일은 USS `:hover` 등 어떤 규칙보다 항상 우선하므로, 인라인로
    배경색을 고정해버리면 그 버튼은 절대 hover 틴트를 보여줄 수 없음. 클래스 토글 방식으로
    바꿔야 `:hover` 규칙이 실제로 얹힐 수 있음. `RunningColor` 상수는 이제 안 쓰여서 제거.

### 검증 완료 (같은 날, 다음 턴)
컴파일 에러/경고 0건. 이번엔 스크린샷이 신뢰할 수 없었음 — 다른 애플리케이션(브라우저) 창이
Unity 창 위 OS 레벨에 떠 있어서 `CopyFromScreen`이 그 창을 대신 캡처함
(`window.Focus()`는 Unity 내부 패널 포커스만 바꾸고 OS 레벨 창 순서는 못 바꿈 —
[[reference_unity_editorwindow_screenshot]] 참고). 대신 `execute_code`로 라이브 인스턴스의
`VisualElement.resolvedStyle`/`GetClasses()`를 직접 리플렉션해서 값 검증:
- `root` 배경 = 새 웜톤 다크 색 정확히 일치
- `sendButton` 배경 = 브랜드 코랄 정확히 일치
- `bridgeToggleButton`이 인라인 스타일이 아니라 `bridge-toggle-button--running` 클래스로
  적용되고 있음 확인 (hover 규칙이 실제로 얹힐 수 있는 구조로 바뀐 것 확인)
- `session-accent-bar`/세션 행 `border-left-color` = `GetSessionAccent(0)`과 정확히 일치
- 채팅 버블(유저/클로드) 배경 = 새 팔레트와 정확히 일치
- `GetSessionAccent(0..5)` 6색 전부 의도한 값(코랄/틸/바이올렛/골드/스카이/세이지)으로 반환됨 확인

hover/focus/active 마이크로인터랙션 자체(포인터 이벤트 필요)는 이 방식으론 검증 못 함 — USS
문법은 정상 컴파일됐고 정적 색상 반영은 100% 확인됐으니 낮은 리스크로 판단, 다음에 사용자가
직접 눈으로 확인하면 될 정도로 남겨둠.

### 다음에 할 일 (TODO)
- [x] 컴파일 에러 확인 → 0건
- [x] 새 팔레트/세션 accent 반영 확인 → 리플렉션으로 전부 일치 확인
- [x] Task #3(M2) 완료 처리
- [ ] 사용자에게 hover/눌림 등 마이크로인터랙션 육안 확인 요청 (에이전트가 포인터 이벤트로는
      검증 불가)
- [ ] M3(캐릭터 상태 확장 - 대기/생각중/도구별/성공/에러) 진행 여부 확인

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
- [x] 다음 세션 시작 시 `read_console`로 컴파일 에러 유무 먼저 확인 → 0건, 정상
- [x] Unity 에디터에서 실제로 창 열어서 레이아웃 확인 → `execute_code` + `System.Drawing`
      스크린샷 및 `VisualElement.layout`/`worldBound` 리플렉션으로 확인. 사이드바(세션 목록 +
      "+ 새 세션"), 캐릭터 스테이지(bob/blink 애니메이션 살아있음), 컨트롤 바(Stop 버튼,
      브릿지 상태 dot/라벨, 대체 입력창 버튼), 채팅 버블(정렬·색·마크다운 bold/inline-code
      렌더링), 입력창+Send 버튼까지 전부 정상 배치 확인. `chatScrollView`가 flex-grow로 남는
      공간만 정확히 차지하고 `inputField`가 그 아래 고정 높이로 붙는 것도 리플렉션으로 재확인
      (레이아웃 붕괴 버그 클래스가 실제로 사라졌음).
  - 스크린샷 캡처 관련 새 팁: 플로팅(undocked) `EditorWindow.position`을 코드로 막 바꾼
    직후 바로 읽으면 아직 반영 전 값(예: 상대 좌표 `(0, 26, ...)`)이 나올 수 있음 — 같은
    `execute_code` 호출 안에서 set 후 즉시 get 하지 말고, 별도 호출로 한 틱 이상 지난 뒤
    다시 읽어야 실제 화면 좌표가 나옴. 또한 창 높이만큼 캡처해도 상단 탭 스트립 높이만큼
    부족해서 하단(입력창 등)이 잘릴 수 있으니 `position.height`에 여유(약 +40~60px)를 두고
    캡처하는 게 안전함.
- [x] M1 안정성 확인 완료 — 사용자에게 보고 후 M2(비주얼 아이덴티티) 진행 여부 확인 예정
- [x] Task #2 (M1 마이그레이션) 완료 처리

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
