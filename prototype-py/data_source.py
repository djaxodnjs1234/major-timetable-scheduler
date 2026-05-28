"""자동 생성된 교과목/교수/강의실 데이터 — 직접 편집 가능.

COURSES가 비어있지 않으면 main.py가 xlsx 대신 이 파일을 로드합니다.
GUI의 '📤 코드 내보내기' 버튼이 이 파일을 덮어씁니다.
"""
from domain.models import (
    Course, Professor, Room, CrossGroup, RetakeScenario,
)

PROFESSORS = [
    Professor(id='김병만', name='김병만', unavailable_slots=[(0, 1), (0, 2), (0, 3), (0, 4), (4, 6), (4, 7), (4, 8), (4, 9)], allowed_rooms=['D327', 'D330', 'D331', 'D331-1']),
    Professor(id='김선명', name='김선명', unavailable_slots=[(2, 6), (2, 7), (2, 8), (2, 9), (4, 6), (4, 7), (4, 8), (4, 9)], allowed_rooms=['D438', 'D440']),
    Professor(id='김성렬', name='김성렬', unavailable_slots=[(4, 1), (4, 2), (4, 3), (4, 4), (4, 6), (4, 7), (4, 8), (4, 9)], allowed_rooms=[]),
    Professor(id='김시관', name='김시관', unavailable_slots=[(4, 1), (4, 2), (4, 3), (4, 4), (4, 6), (4, 7), (4, 8), (4, 9)], allowed_rooms=[]),
    Professor(id='김영우', name='김영우', unavailable_slots=[(0, 1), (0, 2), (0, 3), (0, 4), (4, 6), (4, 7), (4, 8), (4, 9)], allowed_rooms=[]),
    Professor(id='김영원', name='김영원', unavailable_slots=[(0, 1), (0, 2), (0, 3), (0, 4), (4, 6), (4, 7), (4, 8), (4, 9)], allowed_rooms=[]),
    Professor(id='이광희', name='이광희', unavailable_slots=[(4, 6), (4, 7), (4, 8), (4, 9)], allowed_rooms=[]),
    Professor(id='이규원', name='이규원', unavailable_slots=[], allowed_rooms=[]),
    Professor(id='이미영', name='이미영', unavailable_slots=[], allowed_rooms=[]),
    Professor(id='이종열', name='이종열', unavailable_slots=[], allowed_rooms=[]),
    Professor(id='이해연', name='이해연', unavailable_slots=[(0, 1), (0, 2), (0, 3), (0, 4), (0, 6), (0, 7), (0, 8), (0, 9)], allowed_rooms=[]),
    Professor(id='이현아', name='이현아', unavailable_slots=[(4, 1), (4, 2), (4, 3), (4, 4), (4, 6), (4, 7), (4, 8), (4, 9)], allowed_rooms=[]),
    Professor(id='이혜숙', name='이혜숙', unavailable_slots=[], allowed_rooms=[]),
    Professor(id='전태수', name='전태수', unavailable_slots=[(4, 1), (4, 2), (4, 3), (4, 4), (4, 6), (4, 7), (4, 8), (4, 9)], allowed_rooms=[]),
    Professor(id='엄교수', name='엄교수', unavailable_slots=[], allowed_rooms=[]),
]

ROOMS = [
    Room(id='D327', name='D327'),
    Room(id='D330', name='D330'),
    Room(id='D331', name='D331'),
    Room(id='D331-1', name='D331-1'),
    Room(id='D332', name='D332'),
    Room(id='D438', name='D438'),
    Room(id='D440', name='D440'),
    Room(id='DB116', name='DB116'),
    Room(id='DB134', name='DB134'),
    Room(id='K313', name='K313'),
]

COURSES = [
    Course(id='GA1006-01', name='데이터베이스', grade=2, hours_per_week=3, course_type='전필', professor_id='김선명', section=1, department='소프트웨어전공 2학년', fixed_rooms=[], block_structure=[2, 1], is_fixed=False, fixed_slots=[], coteach_profs=[]),
    Course(id='GA1006-02', name='데이터베이스', grade=2, hours_per_week=3, course_type='전필', professor_id='김선명', section=2, department='소프트웨어전공 2학년', fixed_rooms=[], block_structure=[2, 1], is_fixed=False, fixed_slots=[], coteach_profs=[]),
    Course(id='GA1007-01', name='프로그래밍응용', grade=2, hours_per_week=2, course_type='전필', professor_id='김시관', section=1, department='소프트웨어전공 2학년', fixed_rooms=[], block_structure=[2], is_fixed=False, fixed_slots=[], coteach_profs=[]),
    Course(id='GA1007-02', name='프로그래밍응용', grade=2, hours_per_week=2, course_type='전필', professor_id='전태수', section=2, department='소프트웨어전공 2학년', fixed_rooms=[], block_structure=[2], is_fixed=False, fixed_slots=[], coteach_profs=[]),
    Course(id='GA1014-01', name='객체지향소프트웨어공학', grade=3, hours_per_week=3, course_type='전필', professor_id='이종열', section=1, department='소프트웨어전공 3학년', fixed_rooms=[], block_structure=[2, 1], is_fixed=False, fixed_slots=[], coteach_profs=[]),
    Course(id='GA1014-02', name='객체지향소프트웨어공학', grade=3, hours_per_week=3, course_type='전필', professor_id='이종열', section=2, department='소프트웨어전공 3학년', fixed_rooms=[], block_structure=[2, 1], is_fixed=False, fixed_slots=[], coteach_profs=[]),
    Course(id='GA1015-01', name='창의프로젝트', grade=3, hours_per_week=2, course_type='전필', professor_id='이규원', section=1, department='소프트웨어전공 3학년', fixed_rooms=[], block_structure=[2], is_fixed=False, fixed_slots=[], coteach_profs=[]),
    Course(id='GA1015-02', name='창의프로젝트', grade=3, hours_per_week=2, course_type='전필', professor_id='이규원', section=2, department='소프트웨어전공 3학년', fixed_rooms=[], block_structure=[2], is_fixed=False, fixed_slots=[], coteach_profs=[]),
    Course(id='GA1016-01', name='임베디드컴퓨팅', grade=3, hours_per_week=3, course_type='전선', professor_id='전태수', section=1, department='소프트웨어전공 3학년', fixed_rooms=[], block_structure=[2, 1], is_fixed=False, fixed_slots=[], coteach_profs=[]),
    Course(id='GA1017-01', name='인공지능', grade=3, hours_per_week=3, course_type='전선', professor_id='김영우', section=1, department='소프트웨어전공 3학년', fixed_rooms=[], block_structure=[2, 1], is_fixed=False, fixed_slots=[], coteach_profs=[]),
    Course(id='GA1017-02', name='인공지능', grade=3, hours_per_week=3, course_type='전선', professor_id='김영우', section=2, department='소프트웨어전공 3학년', fixed_rooms=[], block_structure=[2, 1], is_fixed=False, fixed_slots=[], coteach_profs=[]),
    Course(id='GA1019-01', name='컴퓨터그래픽스', grade=3, hours_per_week=3, course_type='전선', professor_id='김영원', section=1, department='소프트웨어전공 3학년', fixed_rooms=[], block_structure=[2, 1], is_fixed=False, fixed_slots=[], coteach_profs=[]),
    Course(id='GA1020-01', name='윈도우즈프로그래밍', grade=3, hours_per_week=3, course_type='전선', professor_id='김영원', section=1, department='소프트웨어전공 3학년', fixed_rooms=[], block_structure=[2, 1], is_fixed=False, fixed_slots=[], coteach_profs=[]),
    Course(id='GA1021-01', name='웹서버프로그래밍', grade=3, hours_per_week=3, course_type='전선', professor_id='김성렬', section=1, department='소프트웨어전공 3학년', fixed_rooms=[], block_structure=[2, 1], is_fixed=False, fixed_slots=[], coteach_profs=[]),
    Course(id='GA1030-01', name='알고리즘', grade=4, hours_per_week=3, course_type='전선', professor_id='이현아', section=1, department='소프트웨어전공 4학년', fixed_rooms=[], block_structure=[2, 1], is_fixed=False, fixed_slots=[], coteach_profs=[]),
    Course(id='GA1031-01', name='웹프로젝트실무', grade=4, hours_per_week=3, course_type='전선', professor_id='김성렬', section=1, department='소프트웨어전공 4학년', fixed_rooms=[], block_structure=[2, 1], is_fixed=False, fixed_slots=[], coteach_profs=[]),
    Course(id='GA1033-01', name='4차산업핵심기술', grade=4, hours_per_week=3, course_type='전선', professor_id='김시관', section=1, department='소프트웨어전공 4학년', fixed_rooms=[], block_structure=[2, 1], is_fixed=False, fixed_slots=[], coteach_profs=[]),
    Course(id='GA1034-01', name='빅데이터', grade=4, hours_per_week=3, course_type='전선', professor_id='이광희', section=1, department='소프트웨어전공 4학년', fixed_rooms=[], block_structure=[2, 1], is_fixed=False, fixed_slots=[], coteach_profs=[]),
    Course(id='GA1004-01', name='자료구조', grade=2, hours_per_week=4, course_type='전필', professor_id='이현아', section=1, department='소프트웨어전공 2학년', fixed_rooms=[], block_structure=[2, 2], is_fixed=False, fixed_slots=[], coteach_profs=[]),
    Course(id='GA1004-02', name='자료구조', grade=2, hours_per_week=4, course_type='전필', professor_id='이현아', section=2, department='소프트웨어전공 2학년', fixed_rooms=[], block_structure=[2, 2], is_fixed=False, fixed_slots=[], coteach_profs=[]),
    Course(id='GA1005-01', name='컴퓨터구조', grade=2, hours_per_week=3, course_type='전필', professor_id='이해연', section=1, department='소프트웨어전공 2학년', fixed_rooms=[], block_structure=[2, 1], is_fixed=False, fixed_slots=[], coteach_profs=[]),
    Course(id='GA1005-02', name='컴퓨터구조', grade=2, hours_per_week=3, course_type='전필', professor_id='이해연', section=2, department='소프트웨어전공 2학년', fixed_rooms=[], block_structure=[2, 1], is_fixed=False, fixed_slots=[], coteach_profs=[]),
    Course(id='GA1029', name='캡스톤디자인2', grade=4, hours_per_week=3, course_type='전필', professor_id='김병만', section=1, department='소프트웨어전공 4학년', fixed_rooms=[], block_structure=[3], is_fixed=True, fixed_slots=[(2, 1), (2, 2), (2, 3)], coteach_profs=['김성렬', '김영우', '이광희', '이해연', '전태수']),
    Course(id='USR001', name='이산수학', grade=1, hours_per_week=3, course_type='교양', professor_id='이미영', section=1, department='', fixed_rooms=[], block_structure=[2, 1], is_fixed=False, fixed_slots=[], coteach_profs=[]),
    Course(id='USR002', name='디지털문해력', grade=1, hours_per_week=3, course_type='전필', professor_id='김선명', section=1, department='', fixed_rooms=[], block_structure=[2, 1], is_fixed=False, fixed_slots=[], coteach_profs=[]),
    Course(id='USR003', name='학과회의', grade=4, hours_per_week=1, course_type='전필', professor_id='김병만', section=1, department='', fixed_rooms=[], block_structure=[1], is_fixed=True, fixed_slots=[(2, 4)], coteach_profs=['김선명', '김성렬', '김시관', '김영우', '김영원', '이광희', '이규원', '이종열', '이해연', '이현아', '전태수']),
    Course(id='GA1018', name='모바일프로그래밍', grade=3, hours_per_week=3, course_type='전선', professor_id='김시관', section=1, department='소프트웨어전공 3학년', fixed_rooms=['D330'], block_structure=[2, 1], is_fixed=False, fixed_slots=[], coteach_profs=[]),
]

CROSSES = [
    CrossGroup(id='X001', base_ids=['GA1005', 'GA1006']),
]

RETAKES = [
]
