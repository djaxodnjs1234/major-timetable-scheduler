"""Visual constants — colors, fonts, layout dims.

WPF 이식 시 이 값들이 ResourceDictionary 의 후보. 색은 그대로,
요일/교시 라벨도 동일.
"""
DAY_NAMES = ["월", "화", "수", "목", "금"]
DAYS = 5
PERIODS = list(range(1, 10))
LUNCH = 5
GRADES = [1, 2, 3, 4]

GRADE_COLORS = {
    1: "#FFF9C4",  # 연한 노란색
    2: "#DCEDC8",  # 연두색
    3: "#BBDEFB",  # 하늘색
    4: "#FFCDD2",  # 연한 빨간색
}
EMPTY_BG = "white"
LUNCH_BG = "#E8E8E8"
HEADER_BG = "#F0F0F0"
FIXED_LIST_BG = "#FFFACD"  # 고정 과목 표시용 연노랑

COURSE_TYPES = ["전필", "전선", "교양"]
