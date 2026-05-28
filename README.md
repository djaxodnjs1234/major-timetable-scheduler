# 전공 시간표 자동 편성

대학 학과의 전공 강의 시간표를 제약 충족(CSP) 방식으로 자동 편성. **OR-Tools CP-SAT** 솔버 기반.

## 저장소 구성

```
.
├── prototype-py/   Python 프로토타입 (CP-SAT + Tkinter GUI) — 회귀 베이스라인
├── wpf/            C#/WPF 본 제품 — 현재 포팅 진행 중
├── docs/           공유 설계 문서 + UI 디자인 목업
├── AGENTS.md       Codex 협업 가이드 + 프로젝트 컨텍스트
├── CLAUDE.md       (legacy) Claude 협업 가이드
└── MILESTONE.md    진행 마일스톤
```

## prototype-py — Python (CP-SAT + Tkinter)

OR-Tools CP-SAT 솔버 + Tkinter GUI. WPF 이식을 고려한 layered + UI-agnostic ViewModel 구조로 작성됨. 현재는 **회귀 베이스라인으로 동결**되어 WPF 포팅 결과 검증용으로 사용.

```bash
cd prototype-py
pip install -r requirements.txt
python main.py
```

상세 설계: [AGENTS.md](AGENTS.md)

## wpf — C#/WPF

본 제품 구현. 솔루션 파일 `wpf/TimetableScheduler.slnx`.

```
wpf/
├── TimetableScheduler.Domain     순수 도메인
├── TimetableScheduler.Data       데이터 로더
├── TimetableScheduler.Solver     CP-SAT 솔버 래퍼
├── TimetableScheduler.Scoring    SC 점수/랭킹
├── TimetableScheduler.ViewModel  UI-agnostic 상태
├── TimetableScheduler.Wpf        WPF 셸
└── TimetableScheduler.Tests
```

Visual Studio 또는 `dotnet build wpf/TimetableScheduler.slnx`.

## 핵심 도메인 규칙

- 강의실 충돌: 과목 명시 방이 교수 `allowed_rooms` 보다 우선
- 시간 슬롯: 월~금, 1~9교시 (5교시=점심 금지)
- 21개 하드 제약(HC) + 3개 소프트 제약(SC) 4단계 lex 최적화

자세한 HC/SC 매핑·결정 변수·솔버 흐름은 [AGENTS.md](AGENTS.md) 참조.
