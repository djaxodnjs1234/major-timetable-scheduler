"""UI layer (Tkinter) — viewmodel 은 UI-agnostic, 나머지는 Tkinter 의존.

WPF 포팅 시 viewmodel.py 만 그대로 옮기고 (또는 C# 으로 1:1 포팅) 이
디렉토리 나머지는 XAML/C# 으로 교체.
"""
from .app import launch
from .viewmodel import AppState

__all__ = ["launch", "AppState"]
