import win32file, win32pipe
import threading
import time
import json # 導入 json 模組

pipe_name = r'\\.\pipe\HighPerfPipe'
handle = None

try:
    print("連接到 C# Named Pipe...")
    handle = win32file.CreateFile(
        pipe_name,
        win32file.GENERIC_READ | win32file.GENERIC_WRITE,
        0, None,
        win32file.OPEN_EXISTING,
        0, None
    )

    print("連接成功！")

    def reader():
        """此線程持續讀取從 C# 伺服器傳回的數據"""
        while True:
            try:
                # 讀取從 C# 收到的 JSON 數據
                result, data = win32file.ReadFile(handle, 4096)
                if data:
                    # 解碼並嘗試解析 JSON
                    try:
                        received_json = json.loads(data.decode('utf-8').strip())
                        print("收到 JSON 數據:", received_json)
                    except json.JSONDecodeError:
                        print("收到 (非 JSON):", data.decode('utf-8').strip())

            except Exception as e:
                print(f"讀取錯誤，可能連線已中斷: {e}")
                break

    threading.Thread(target=reader, daemon=True).start()

    while True:

        # 創建要發送的 JSON 對象
        data_to_send = {
            "command": "GET_STATE".strip(),
            "timestamp": time.time()
        }

        # 將 Python 字典轉換為 JSON 字符串
        json_string = json.dumps(data_to_send)

        # 確保發送的訊息以換行符結尾，以便 C# 的 ReadLine() 能正常工作
        msg = f"{json_string}\r\n"
        print(f"發送 JSON: {json_string}")

        try:
            win32file.WriteFile(handle, msg.encode('utf-8'))
        except Exception as e:
            print(f"寫入錯誤，可能連線已中斷: {e}")
            break

finally:
    if handle:
        win32file.CloseHandle(handle)
        print("連線已關閉。")