import ctypes
from ctypes import wintypes
import win32gui, win32ui
from PIL import Image

# 宣告 PrintWindow
user32 = ctypes.windll.user32
user32.PrintWindow.argtypes = [wintypes.HWND, wintypes.HDC, wintypes.UINT]
user32.PrintWindow.restype = wintypes.BOOL

def capture_window(hwnd, filename="screenshot.png"):
    # 取得視窗矩形
    left, top, right, bot = win32gui.GetWindowRect(hwnd)
    width, height = right - left, bot - top

    # 建立 DC
    hwindc = win32gui.GetWindowDC(hwnd)
    srcdc = win32ui.CreateDCFromHandle(hwindc)
    memdc = srcdc.CreateCompatibleDC()

    # 建立位圖
    bmp = win32ui.CreateBitmap()
    bmp.CreateCompatibleBitmap(srcdc, width, height)
    memdc.SelectObject(bmp)

    # 呼叫 PrintWindow
    result = user32.PrintWindow(hwnd, memdc.GetSafeHdc(), 0)

    # 轉成 PIL 圖片
    bmpinfo = bmp.GetInfo()
    bmpstr = bmp.GetBitmapBits(True)
    im = Image.frombuffer(
        'RGB',
        (bmpinfo['bmWidth'], bmpinfo['bmHeight']),
        bmpstr, 'raw', 'BGRX', 0, 1
    )
    im.save(filename)

    # 清理
    srcdc.DeleteDC()
    memdc.DeleteDC()
    win32gui.ReleaseDC(hwnd, hwindc)
    win32gui.DeleteObject(bmp.GetHandle())

    return result == 1

# 範例：找 osu! 視窗
def find_osu_hwnd():
    targets = []
    def enum_handler(hwnd, _):
        if win32gui.IsWindowVisible(hwnd):
            title = win32gui.GetWindowText(hwnd)
            if title and ("osu!" in title.lower() or "lazer" in title.lower()):
                targets.append((hwnd, title))
    win32gui.EnumWindows(enum_handler, None)
    return targets[0][0] if targets else None

if __name__ == "__main__":
    hwnd = find_osu_hwnd()
    if hwnd:
        ok = capture_window(hwnd, "osu_lazer.png")
        print("成功" if ok else "PrintWindow 可能失敗或黑畫面")
    else:
        print("找不到 osu! 視窗")