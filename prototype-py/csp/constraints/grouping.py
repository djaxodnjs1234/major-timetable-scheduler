"""Group-level HCs that span multiple courses.

- HC-16 : Cross (분반 시간 cyclic shift 동기화)
- HC-17 : Retake (재수강 안전 분반)
"""
from ._common import DAYS, VALID_PERIODS, _base_id


def add_hc16_cross(model, y, courses, crosses):
    """HC-16: 묶인 과목들의 분반 시간 동기화 (cyclic shift).

    cross_group.base_ids = [A, B, ...] 각 과목 K개 분반.
    과목 i 의 sec_k 와 과목 (i+1) 의 sec_{(k+1) % K} 의 점유 시간이 동일.
    슬롯 점유 indicator y 로 비교 (다중방 시 sum_r x 가 K 배 카운트되는 문제 회피).
    """
    if not crosses:
        return
    base_groups = {}
    for c in courses:
        base_groups.setdefault(_base_id(c.id), []).append(c)
    for bid in list(base_groups.keys()):
        base_groups[bid] = sorted(base_groups[bid], key=lambda c: c.section)

    for cross in crosses:
        bids = list(cross.base_ids)
        if len(bids) < 2:
            continue
        groups = [base_groups.get(b, []) for b in bids]
        if any(len(g) == 0 for g in groups):
            continue
        K = min(len(g) for g in groups)
        if K < 1:
            continue
        for i in range(len(bids) - 1):
            g1 = groups[i]
            g2 = groups[i + 1]
            for k in range(K):
                c1 = g1[k]
                c2 = g2[(k + 1) % K]
                for d in range(DAYS):
                    for p in VALID_PERIODS:
                        model.Add(y[(c1.id, d, p)] == y[(c2.id, d, p)])


def add_hc17_retake(model, y, courses, retakes):
    """HC-17: 재수강 학생을 위한 안전 분반 보장.

    각 (G, R) 시나리오: R 의 분반 중 적어도 1개는 G 학년의 모든 전공필수와
    시간이 안 겹쳐야 한다. y 는 슬롯 점유 indicator.
    """
    if not retakes:
        return
    base_groups = {}
    for c in courses:
        base_groups.setdefault(_base_id(c.id), []).append(c)

    for sc in retakes:
        G = sc.current_grade
        R = sc.retake_base_id
        sections = base_groups.get(R, [])
        if not sections:
            continue
        majors = [c for c in courses
                  if c.grade == G and c.course_type == "전필"
                  and _base_id(c.id) != R]
        if not majors:
            continue

        safe_vars = []
        for s in sections:
            conflict_vars = []
            for m in majors:
                for d in range(DAYS):
                    for p in VALID_PERIODS:
                        s_uses = y[(s.id, d, p)]
                        m_uses = y[(m.id, d, p)]
                        cf = model.NewBoolVar(
                            f"retake_cf_{R}_{s.id}_{m.id}_{d}_{p}")
                        model.Add(s_uses + m_uses - 1 <= cf)
                        model.Add(cf <= s_uses)
                        model.Add(cf <= m_uses)
                        conflict_vars.append(cf)
            any_cf = model.NewBoolVar(f"retake_any_{R}_{s.id}")
            if conflict_vars:
                model.AddMaxEquality(any_cf, conflict_vars)
            else:
                model.Add(any_cf == 0)
            safe = model.NewBoolVar(f"retake_safe_{R}_{s.id}")
            model.Add(safe + any_cf == 1)
            safe_vars.append(safe)

        model.Add(sum(safe_vars) >= 1)
