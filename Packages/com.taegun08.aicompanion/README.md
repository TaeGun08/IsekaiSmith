# AI Companion

Unity 에디터 안에서 Claude / Codex / Cursor CLI와 채팅하는 개발용 동반자 도구입니다.
[Unity MCP](https://github.com/CoplayDev/unity-mcp)가 설치되어 있으면 AI가 에디터를 직접 조작(스크립트
작성, GameObject 조작, 콘솔 확인 등)하도록 툴콜을 열어줄 수 있지만, **Unity MCP는 필수가 아닙니다** -
없어도 채팅 자체는 정상 동작합니다.

## 다른 프로젝트에 설치하기

이 폴더(`Packages/com.taegun08.aicompanion`) 전체를 그대로 대상 프로젝트의 `Packages/` 폴더 아래에
복사해 넣으면 끝입니다. Unity가 자동으로 로컬 패키지로 인식합니다. 대상 프로젝트의 `Assets` 폴더
구조/네이밍 규칙은 전혀 건드리지 않습니다.

## 최초 실행 시 필요한 것

1. **Unity 6000.3 이상**
2. **Node.js / npm** - Claude CLI, Codex CLI 자동 설치에 필요 (`npm install -g` 방식)
3. Claude CLI, Codex CLI, Cursor CLI 중 최소 하나 - 위 npm이 준비되어 있다면 자동 설치 가능
4. (선택) Unity MCP 패키지 - 에디터 조작 툴콜을 쓰려면 설치, 채팅만 쓸 거면 건너뛰어도 됨

`Window > AI Companion`을 처음 열면(또는 패키지를 처음 추가한 직후) **셋업 마법사**가 자동으로 한 번
뜹니다. 항목별로 무엇이 빠졌는지 보여주고, npm 기반 CLI는 버튼 한 번으로 설치, Unity MCP도 버튼 한
번으로 패키지를 추가합니다. Node.js 자체나 Cursor CLI(공식 설치가 `curl | bash` 스크립트라 자동화가
어려움)는 안내 링크만 제공합니다. 마법사는 `Window > AI Companion Setup Wizard`로 언제든 다시 열 수
있습니다.

## 알려진 제약

- CLI 자동 설치는 현재 Windows(`cmd.exe` 기반)만 지원합니다. macOS/Linux에서는 안내에 따라 수동
  설치가 필요합니다.
- UI 라벨은 한국어로 고정되어 있습니다.
