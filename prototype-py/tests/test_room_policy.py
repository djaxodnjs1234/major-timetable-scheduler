"""강의실 배정 정책 synthetic 검증.

[docs/room_test_cases.md](../docs/room_test_cases.md) 의 TC-04, 05, 06, 09, 10,
11 을 자동화. 프로젝트 루트에서 ``python -m tests.test_room_policy`` 또는
``python tests/test_room_policy.py`` 로 실행.
"""
import os
import sys

# 프로젝트 루트를 sys.path 에 추가 (tests/ 에서 직접 실행 시)
_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if _ROOT not in sys.path:
    sys.path.insert(0, _ROOT)

from domain.models import Course, Professor, Room
from csp import build_and_solve_diverse


def run(name, courses, profs, rooms, expect_fn, ts=10, ps=5):
    status, sols = build_and_solve_diverse(
        courses, profs, rooms,
        total_solutions=1, time_limit_sec=ts, per_solve_time_sec=ps)
    if not sols:
        print(f'[{name}] FAIL: status={status}, no sol')
        return False
    ok, msg = expect_fn(sols[0])
    print(f'[{name}] {"PASS" if ok else "FAIL"}: {msg}')
    return ok


results = []

# TC-04: 단일방 명시 — 교수 무시
profs = [Professor(id='P1', name='P1', allowed_rooms=['R3'])]
rooms = [Room(id='R1', name='R1'), Room(id='R2', name='R2'), Room(id='R3', name='R3')]
courses = [
    Course(id='C1', name='X', grade=3, hours_per_week=2,
           professor_id='P1', section=1, fixed_rooms=['R1'],
           block_structure=[2]),
]
def chk04(a):
    rs = sorted(set(r for (_, _, _, r) in a))
    return rs == ['R1'], f'used={rs}'
results.append(('TC-04', run('TC-04', courses, profs, rooms, chk04)))

# TC-05: 다중방 6개 동시
profs = [Professor(id='P1', name='P1')]
rooms = [Room(id=f'R{i}', name=f'R{i}') for i in range(1, 7)]
courses = [
    Course(id='Cap', name='Cap', grade=4, hours_per_week=2,
           professor_id='P1', section=1,
           fixed_rooms=['R1', 'R2', 'R3', 'R4', 'R5', 'R6'],
           block_structure=[2]),
]
def chk05(a):
    rs = sorted(set(r for (_, _, _, r) in a))
    times = sorted(set((d, p) for (_, d, p, _) in a))
    return (rs == ['R1', 'R2', 'R3', 'R4', 'R5', 'R6']
            and len(a) == 12 and len(times) == 2,
            f'rooms={rs}, total={len(a)}, slots={len(times)}')
results.append(('TC-05', run('TC-05', courses, profs, rooms, chk05)))

# TC-06: 과목·교수 충돌 — 과목 우선
profs = [Professor(id='P1', name='P1', allowed_rooms=['R2'])]
rooms = [Room(id='R1', name='R1'), Room(id='R2', name='R2')]
courses = [
    Course(id='C1', name='X', grade=3, hours_per_week=2,
           professor_id='P1', section=1, fixed_rooms=['R1'],
           block_structure=[2]),
]
def chk06(a):
    rs = sorted(set(r for (_, _, _, r) in a))
    return rs == ['R1'], f'used={rs}'
results.append(('TC-06', run('TC-06', courses, profs, rooms, chk06)))

# TC-09: 같은 main + 자동/명시 혼합
profs = [Professor(id='P1', name='P1')]
rooms = [Room(id=f'R{i}', name=f'R{i}') for i in range(1, 5)]
courses = [
    Course(id='A', name='A', grade=3, hours_per_week=2,
           professor_id='P1', section=1, fixed_rooms=[],
           block_structure=[2]),
    Course(id='B', name='B', grade=3, hours_per_week=2,
           professor_id='P1', section=1, fixed_rooms=[],
           block_structure=[2]),
    Course(id='C', name='C', grade=3, hours_per_week=2,
           professor_id='P1', section=1, fixed_rooms=['R3'],
           block_structure=[2]),
]
def chk09(a):
    by = {}
    for (cid, d, p, r) in a:
        by.setdefault(cid, set()).add(r)
    a_room = by['A']
    b_room = by['B']
    c_room = by['C']
    same_ab = a_room == b_room and len(a_room) == 1
    c_ok = c_room == {'R3'}
    return (same_ab and c_ok,
            f'A={sorted(a_room)}, B={sorted(b_room)}, C={sorted(c_room)}')
results.append(('TC-09', run('TC-09', courses, profs, rooms, chk09)))

# TC-10: 팀티칭 — main 만 따라감
profs = [
    Professor(id='P1', name='P1', allowed_rooms=['R1']),
    Professor(id='P2', name='P2', allowed_rooms=['R2']),
]
rooms = [Room(id='R1', name='R1'), Room(id='R2', name='R2')]
courses = [
    Course(id='X', name='X', grade=3, hours_per_week=2,
           professor_id='P1', coteach_profs=['P2'], section=1,
           fixed_rooms=[], block_structure=[2]),
    Course(id='Y', name='Y', grade=3, hours_per_week=2,
           professor_id='P2', section=1, fixed_rooms=[],
           block_structure=[2]),
]
def chk10(a):
    by = {}
    for (cid, d, p, r) in a:
        by.setdefault(cid, set()).add(r)
    return (by['X'] == {'R1'} and by['Y'] == {'R2'},
            f'X={sorted(by["X"])}, Y={sorted(by["Y"])}')
results.append(('TC-10', run('TC-10', courses, profs, rooms, chk10)))

# TC-11: main 자동 과목 없음
profs = [Professor(id='P1', name='P1', allowed_rooms=[])]
rooms = [Room(id='R1', name='R1'), Room(id='R2', name='R2')]
courses = [
    Course(id='C1', name='X', grade=3, hours_per_week=2,
           professor_id='P1', section=1, fixed_rooms=['R1'],
           block_structure=[2]),
]
def chk11(a):
    rs = sorted(set(r for (_, _, _, r) in a))
    return rs == ['R1'], f'used={rs}'
results.append(('TC-11', run('TC-11', courses, profs, rooms, chk11)))

# TC-19: cross 그룹 멤버 블록 시작 교시 ∈ {1,3,6,8}
from domain.models import CrossGroup
profs = [Professor(id='P1', name='P1'), Professor(id='P2', name='P2')]
rooms = [Room(id=f'R{i}', name=f'R{i}') for i in range(1, 5)]
courses = [
    Course(id='A-01', name='A', grade=3, hours_per_week=3,
           professor_id='P1', section=1, fixed_rooms=[],
           block_structure=[2, 1]),
    Course(id='A-02', name='A', grade=3, hours_per_week=3,
           professor_id='P1', section=2, fixed_rooms=[],
           block_structure=[2, 1]),
    Course(id='B-01', name='B', grade=3, hours_per_week=3,
           professor_id='P2', section=1, fixed_rooms=[],
           block_structure=[2, 1]),
    Course(id='B-02', name='B', grade=3, hours_per_week=3,
           professor_id='P2', section=2, fixed_rooms=[],
           block_structure=[2, 1]),
]
crosses = [CrossGroup(id='X1', base_ids=['A', 'B'])]
ALLOWED_PAIR_STARTS = {1, 3, 6, 8}
def chk19(a):
    """길이 2 블록은 시작 ∈ {1,3,6,8}; 분반 페어 길이 1 블록은 페어 작은 sp ∈ {1,3,6,8}."""
    # 과목·날짜별로 점유 슬롯 모음
    by_cd = {}
    for (cid, d, p, _) in a:
        by_cd.setdefault((cid, d), set()).add(p)

    # 길이 2 블록 시작 검증 (A-01/02, B-01/02 의 [2] 부분)
    for (cid, d), ps in by_cd.items():
        sps = sorted(ps)
        runs = []
        i = 0
        while i < len(sps):
            j = i
            while j + 1 < len(sps) and sps[j + 1] == sps[j] + 1:
                j += 1
            runs.append((sps[i], j - i + 1))
            i = j + 1
        for (sp, length) in runs:
            if length == 2 and sp not in ALLOWED_PAIR_STARTS:
                return False, f'{cid} day={d} 길이2 블록 sp={sp} not in {{1,3,6,8}}'

    # 분반 페어 길이 1 블록 — A-01/A-02 의 1-블록 페어 동일 날, 인접 sp, 작은 sp ∈ {1,3,6,8}
    for base in ('A', 'B'):
        sec1, sec2 = f'{base}-01', f'{base}-02'
        # 각 분반의 점유 중 길이 1 블록(고립 슬롯) 식별
        def isolated(cid):
            iso = []
            for (c, d), ps in by_cd.items():
                if c != cid:
                    continue
                sps = sorted(ps)
                i = 0
                while i < len(sps):
                    j = i
                    while j + 1 < len(sps) and sps[j + 1] == sps[j] + 1:
                        j += 1
                    if j == i:  # 길이 1
                        iso.append((d, sps[i]))
                    i = j + 1
            return iso
        iso1 = isolated(sec1)
        iso2 = isolated(sec2)
        # 각 (d, sp1) 페어의 sp2 = sp1±1 + 페어 작은 ∈ {1,3,6,8}
        for (d, sp1) in iso1:
            partner = next(((dd, pp) for (dd, pp) in iso2
                            if dd == d and abs(pp - sp1) == 1), None)
            if partner is None:
                return False, f'{sec1} day={d} sp={sp1} 분반 페어 인접 partner 없음'
            sp2 = partner[1]
            if min(sp1, sp2) not in ALLOWED_PAIR_STARTS:
                return False, f'{sec1}/{sec2} day={d} 페어 (sp1={sp1}, sp2={sp2}) 작은 sp ∉ {{1,3,6,8}}'
    return True, '길이2 시작 + cross 분반 페어 작은 sp 모두 정렬'
def run_cross():
    status, sols = build_and_solve_diverse(
        courses, profs, rooms,
        total_solutions=1, time_limit_sec=15, per_solve_time_sec=8,
        crosses=crosses)
    if not sols:
        print(f'[TC-19] FAIL: status={status}')
        return False
    ok, msg = chk19(sols[0])
    print(f'[TC-19] {"PASS" if ok else "FAIL"}: {msg}')
    return ok
results.append(('TC-19 cross 시작 교시', run_cross()))

# 추가 검증: 일반 (cross 없음) [2,1] 의 1-블록은 임의 위치 OK 인지 (회귀 방지)
profs = [Professor(id='P1', name='P1')]
rooms = [Room(id='R1', name='R1'), Room(id='R2', name='R2')]
courses = [
    Course(id='C1', name='X', grade=3, hours_per_week=3,
           professor_id='P1', section=1, fixed_rooms=[],
           block_structure=[2, 1]),
]
def chk_regress(a):
    return True, 'no-cross [2,1] solved (1-블록 위치 자유)'
def run_regress():
    status, sols = build_and_solve_diverse(
        courses, profs, rooms,
        total_solutions=1, time_limit_sec=10, per_solve_time_sec=5)
    return sols and status in ('OPTIMAL', 'FEASIBLE'), \
           f'status={status}, sols={len(sols)}'
ok, msg = run_regress()
print(f'[Regress] {"PASS" if ok else "FAIL"}: {msg}')
results.append(('Regress 비-cross [2,1]', ok))

print()
print('========= SUMMARY =========')
for name, ok in results:
    print(f'  [{("PASS" if ok else "FAIL")}]  {name}')
fails = sum(1 for _, ok in results if not ok)
print(f'\nTotal: {len(results)} cases, {fails} FAIL')
