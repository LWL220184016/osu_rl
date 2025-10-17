import win32gui

titles = []

def enum_handler(hwnd, ctx):
    if win32gui.IsWindowVisible(hwnd):
        title = win32gui.GetWindowText(hwnd)
        if title:  # 過濾掉空標題
            titles.append(title)

win32gui.EnumWindows(enum_handler, None)

for t in titles:
    print(t)