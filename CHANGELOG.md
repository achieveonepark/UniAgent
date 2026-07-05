# Changelog

## [1.0.0] - 2026-07-05

### Added

- `Tools/UniAgent/UniAgent Chat` 에디터 채팅 창 추가
- Codex 및 Claude Code CLI 프로바이더 선택, 설치/로그인 상태 점검, 로그인/로그아웃 흐름 추가
- `Plan`/`Build` 채팅 모드와 Build 전용 Diff Preview 흐름 추가
- 다중 세션 생성/전환, 채팅 이력 영속화, 세션 토큰 사용량 추적 및 토큰 게이지 UI 추가
- `@mention` 파일 자동완성과 타겟 파일 컨텍스트 첨부 기능 추가
- 모델 카탈로그 갱신, 모델별 토큰 예산, 컨텍스트 자동 압축 기능 추가
- Claude/Codex 진행 상태 스트리밍 표시와 진행 문구 언어 설정(`Auto`, `Korean`, `English`) 추가
- Unity Action Bridge 추가: `AddComponent`, `RemoveComponent`, `CreatePrimitiveObject`, `CreatePrimitivePrefab`, `CreateSpriteObject`, `CreateSpritePrefab`, `SavePrefabFromTarget`, `CreateCsvDataTable`
- 새 런타임 `MonoBehaviour` 컴파일 이후 프리팹에 컴포넌트를 붙이기 위한 deferred Unity action 적용 흐름 추가
- Runtime CSV 파서/로더와 백엔드 프록시 클라이언트 계약 추가

### Changed

- 지원되는 Unity 작업은 일회성 자동실행 Editor 스크립트 대신 Unity Action Bridge를 우선 사용하도록 프롬프트와 브리지 기능 정리
- 수동 새로고침 모드에서 스크립트 컴파일 요청 시 Auto Refresh 락을 안전하게 풀었다가 되돌리도록 처리
- Claude Code 실행 시 프로젝트/로컬 설정을 우선 사용하고, 스트리밍 JSON 출력 기반으로 진행 상태를 표시하도록 개선

### Fixed

- Windows에서 npm 전역 설치 CLI(`codex.cmd`, `claude.cmd`)를 Unity에서 직접 실행하지 못하던 문제 수정
- Unity 에디터 프로세스에서 CLI/API 관련 환경 변수를 상속하지 못해 로그인 또는 API 연결이 실패하던 문제 완화
- Claude Code 브라우저 로그인 성공 후 상태 확인이 늦어 실패로 표시되던 문제 수정
- Claude Code 파일 편집 권한 프롬프트가 비대화형 Unity 채팅에서 멈추는 문제를 줄이기 위해 선택형 `Auto-accept Claude file edits` 설정 추가
- 새로 생성된 자동실행 Editor 스크립트(`InitializeOnLoad`, `InitializeOnLoadMethod`, `DidReloadScripts`)가 컴파일 전에 실행되지 않도록 격리 처리 추가
