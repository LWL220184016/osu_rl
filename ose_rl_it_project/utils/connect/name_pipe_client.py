import win32file
import win32pipe
import threading
import time
import json
import pywintypes # 導入 pywintypes 以便捕捉特定的 Windows 錯誤

class NamedPipeClient():
    """
    一個用於與 C# 命名管道伺服器進行通信的客戶端。
    """
    def __init__(self, pipe_name: str):
        """
        初始化客戶端並嘗試連接到伺服器。

        Args:
            pipe_name (str): 管道的名稱，格式為 r'\\.\pipe\PipeName'。
        """
        self.pipe_name = pipe_name
        self.handle = None
        self.is_running = False
        self.reader_thread = None
        self.writer_thread = None
        self.lock = threading.Lock() # 增加一個鎖來確保線程安全

    def connect(self):
        """連接到命名管道伺服器。"""
        try:
            self.handle = win32file.CreateFile(
                self.pipe_name,
                win32file.GENERIC_READ | win32file.GENERIC_WRITE,
                0, None,
                win32file.OPEN_EXISTING,
                0, None
            )
            print("成功連接到命名管道伺服器。")
            self.is_running = True
            return True
        except pywintypes.error as e:
            # ERROR_PIPE_BUSY or ERROR_FILE_NOT_FOUND
            if e.winerror == 231 or e.winerror == 2:
                print(f"無法連接到伺服器，伺服器可能尚未就緒。錯誤碼: {e.winerror}")
            else:
                print(f"連接時發生未知 Windows 錯誤: {e}")
            return False

    def _reader(self):
        """
        此線程持續讀取從 C# 伺服器傳回的數據。
        這是一個內部方法，由 start() 啟動。
        """
        while self.is_running:
            try:
                # 讀取從 C# 收到的數據
                result, data = win32file.ReadFile(self.handle, 4096)
                if data:
                    try:
                        # 解碼並嘗試解析 JSON，移除尾部的空字元
                        received_json = json.loads(data.decode('utf-8').strip('\x00'))
                        print(f"[{time.time():.2f}] 收到 JSON 數據: {json.dumps(received_json, indent=4, ensure_ascii=False)}")
                    except json.JSONDecodeError:
                        print(f"收到 (非 JSON): {data.decode('utf-8', errors='ignore').strip()}")
                    except Exception as e:
                        print(f"處理數據時發生錯誤: {e}")

            except pywintypes.error as e:
                # ERROR_BROKEN_PIPE
                if e.winerror == 109:
                    print("讀取錯誤：管道已由伺服器端關閉。")
                else:
                    print(f"讀取時發生 Windows 錯誤，連線可能已中斷: {e}")
                self.stop() # 連線中斷時停止客戶端
                break
            except Exception as e:
                print(f"讀取時發生未知錯誤: {e}")
                self.stop()
                break

    def _writer(self):
        """
        此線程定時向 C# 伺服器發送數據。
        這是一個內部方法，由 start() 啟動。
        """
        while self.is_running:
            try:
                # 創建要發送的 JSON 對象
                data_to_send = {
                    "command": "GET_STATE",
                    "timestamp": time.time()
                }
                json_string = json.dumps(data_to_send)
                msg = f"{json_string}\n" # C# 的 ReadLine() 通常需要換行符 \n

                # 使用鎖確保寫入操作的原子性
                with self.lock:
                    if self.handle:
                        win32file.WriteFile(self.handle, msg.encode('utf-8'))
                
                # 在循環的其餘部分休眠
                time.sleep(5)

            except pywintypes.error as e:
                print(f"寫入時發生 Windows 錯誤，可能連線已中斷: {e}")
                self.stop() # 連線中斷時停止客戶端
                break
            except Exception as e:
                print(f"寫入時發生未知錯誤: {e}")
                self.stop()
                break

    def start(self):
        """啟動讀取和寫入線程。"""
        if not self.is_running:
            print("客戶端尚未連接。")
            return
        
        self.reader_thread = threading.Thread(target=self._reader, daemon=True)
        self.writer_thread = threading.Thread(target=self._writer, daemon=True)
        self.reader_thread.start()
        self.writer_thread.start()
        print("客戶端讀寫線程已啟動。")

    def stop(self):
        """停止所有操作並關閉連接。"""
        if not self.is_running:
            return

        print("正在停止客戶端...")
        self.is_running = False # 設置旗標，讓線程的循環結束

        # 等待線程結束
        if self.writer_thread and self.writer_thread.is_alive():
            self.writer_thread.join(timeout=1)
        if self.reader_thread and self.reader_thread.is_alive():
            self.reader_thread.join(timeout=1)

        # 關閉管道句柄
        if self.handle:
            with self.lock:
                win32file.CloseHandle(self.handle)
                self.handle = None
        print("客戶端已停止，連線已關閉。")

    def get_is_running(self) -> bool:
        """
        檢查客戶端是否仍在運行（線程是否存活）。

        Returns:
            bool: 如果讀取和寫入線程都存在且正在運行，則返回 True。
        """
        return (self.is_running and
                self.reader_thread and self.reader_thread.is_alive() and
                self.writer_thread and self.writer_thread.is_alive())

if __name__ == "__main__":
    pipe_name = r'\\.\pipe\HighPerfPipe'
    client = None

    try:
        client = NamedPipeClient(pipe_name=pipe_name)
        # 嘗試連接，如果伺服器還沒開，可以稍後重試
        if client.connect():
            client.start()

            # 主線程持續運行，直到用戶中斷 (例如按下 Ctrl+C)
            # 或是讓它運行一段固定時間
            while client.get_is_running():
                time.sleep(1)

    except KeyboardInterrupt:
        print("收到用戶中斷信號。")
    except Exception as e:
        print(f"主程序發生未預期的錯誤: {e}")
    finally:
        if client:
            client.stop()