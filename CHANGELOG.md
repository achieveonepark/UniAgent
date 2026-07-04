# Changelog

## [1.0.6] - 2026-07-04

### Fixed

- Codex/Claude Code CLI가 설치돼 있어도 "CLI was not found"가 뜨던 문제 수정: Unity 에디터(GUI 프로세스)는
  nvm/volta 등이 `.zshrc`/`.bashrc`에 추가한 PATH를 상속받지 못하는 경우가 많아, 내장 후보 경로로도 못 찾으면
  로그인/대화형 셸(`command -v`, macOS/Linux) 또는 `where`(Windows)로 한 번 더 해석하도록 개선
- 내장 CLI 후보 경로에 `~/.volta/bin`, `~/.local/bin`, Windows `%USERPROFILE%\AppData\Roaming\npm` 추가
- README/`package.json`의 저장소 URL이 예전 이름(`unicodex`)을 가리켜 UPM Git URL 설치 시 404가 나던 문제 수정 (`uni-agent`로 정정)

### Added

- 설정 패널에 Codex/Claude Code CLI 경로를 직접 입력할 수 있는 수동 재정의 필드 추가(자동 탐지 실패 시 우회용)

## [1.0.5] - 2026-07-04

### Added

- Unity Helper 메뉴 추가: `Tools/UniAgent/Unity Helper/Write Skill Template` — `.claude/skills/<이름>/SKILL.md` 뼈대 생성(Claude Code가 프로젝트 내에서 자동 인식)
- Unity Helper 메뉴 추가: `Tools/UniAgent/Unity Helper/Check Unity MCP Status` — 프로젝트의 `.mcp.json`에 Unity MCP(공식 `com.unity.ai.assistant` 패키지) 항목이 있는지 확인하고, 없으면 Unity의 Project Settings > AI > Unity MCP > Integrations에서 Claude Code용으로 Configure하도록 안내

## [1.0.4] - 2026-07-04

### Added

- 세션 토큰 사용량이 예산의 80%를 넘으면 최근 대화를 요약해 새 CLI 스레드로 자동 전환하는 컨텍스트 자동 압축 기능 추가
- 어시스턴트 "생각 중" 표시에 경과 시간과 작업 종류별 아이콘(읽기/편집/검색/실행) 추가

## [1.0.3] - 2026-07-04

### Added

- `UniAgentModelCatalog` 추가: 모델 목록/기본 모델/모델별 토큰 예산을 원격 URL(선택)에서 갱신하고 `Library/UniAgentModelCatalogCache.json`에 캐시
- 설정 패널에 `Catalog URL` 입력란과 `Refresh` 버튼 추가

### Changed

- Codex/Claude Code 모델 옵션과 세션 토큰 예산이 하드코딩 상수 대신 `UniAgentModelCatalog`를 통해 조회되도록 변경
- 세션 토큰 예산이 선택된 모델을 기준으로 자동 계산되도록 변경(프로바이더/모델 변경 시 즉시 갱신)
- `UniAgentChatWindow.cs`(4,676줄)를 책임별 partial 파일 11개로 분리(`Panels`, `ChatArea`, `Input`, `Environment`, `Run`, `Messages`, `Sessions`, `Status`, `History`, `UIHelpers`, `Types`)

## [1.0.2] - 2026-07-04

### Changed

- Claude Code 모델 기본값/선택 옵션을 최신 라인업으로 갱신 (`claude-sonnet-4-6` → `claude-sonnet-5`, `claude-opus-4-6` → `claude-opus-4-8`, `claude-haiku-4-5-20251001` 유지)

## [1.0.1] - 2026-05-13

### Added

- Codex 모델 선택 옵션에 `gpt-5.4-codex`, `gpt-5.5-codex` 추가

## [1.0.0] - 2026-03-01

### Added

- 초기 릴리즈: `com.achieve.uni-codex` 패키지 공개
- `Tools/UniAgent/UniAgent Chat` 에디터 채팅 창 추가
- Codex 설치/로그인 상태 점검 및 Device Auth 로그인/로그아웃 흐름 추가
- `Plan`/`Build` 채팅 모드 및 Build 전용 `Diff On` 토글 추가
- `Codex Diff Preview` 창 추가
- 파일별 탭 렌더링, 라인 통계(+/-), 패치 적용(`Apply`) 지원
- 현재 Diff 재정제(`Refine`) 요청 지원
- 프로젝트 외부 경로/위험 경로 차단을 포함한 안전한 패치 적용 로직 추가
- 다중 세션 생성/전환 및 세션별 Codex thread 연동 추가
- 채팅/세션 이력 영속화(`Library/CodexChatHistory.json`) 추가
- `@mention` 파일 자동완성과 타겟 파일 컨텍스트 첨부 기능 추가
- 세션 토큰 사용량 추적 및 원형 토큰 게이지 UI 추가
- Unity Action Bridge 추가 (`Library/CodexUnityActions.json`)
- 지원 액션: `AddComponent`, `RemoveComponent`, `CreateSpriteObject`, `SavePrefabFromTarget`, `CreateCsvDataTable`
- Unity Helper 메뉴 추가: `Tools/Codex/Unity Helper/Apply Pending Actions`
- Unity Helper 메뉴 추가: `Tools/Codex/Unity Helper/Write Action Template`
- Unity Helper 메뉴 추가: `Tools/Codex/Unity Helper/Write CSV Table Template`
- Unity Helper 메뉴 추가: `Tools/Codex/Unity Helper/Open Generated Prefab Folder`
- Unity 메인 툴바 플레이 영역 단축 버튼 자동 주입 및 재설치 메뉴 추가
- Codex CLI 래퍼 서비스 추가(경로 탐색/버전 확인, `codex exec --json` 실행, thread resume)
- Codex CLI 출력에서 진행 상태(progress) 메시지 파싱 추가
- Codex CLI 출력에서 토큰 사용량(input/output/total) 추출 추가
- Runtime CSV 파서/로더 추가: `UniAgentCsvDataTableProvider`
- Runtime 코어 파사드 추가: `UniAgent.Data`, `UniAgent.Client`
- Runtime 백엔드 프록시 클라이언트 추가: `UniAgentBackendProxyClient`, `IUniAgentBackendGateway`
- Editor 실행 경로를 `UniAgent.Client` 계약과 연결(어댑터 기반)
- 모바일/런타임 로그인 방식 명시: 백엔드 세션 토큰 기반 로그인
