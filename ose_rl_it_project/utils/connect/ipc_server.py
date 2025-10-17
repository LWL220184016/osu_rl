import win32file
import win32pipe
import threading
import time
import json
import pywintypes

class NamePipeServer():
    def __init__(self, pipe_name: str):
        """
        初始化命名管道伺服器。

        Args:
            pipe_name (str): 管道的名稱，格式為 \\.\pipe\<pipename>
        """
        self.pipe_name = pipe_name
        self.handle = None
        self.is_running = threading.Event()
        self.reader_thread = None
        self.writer_thread = None

    def reader(self):
        """此線程持續讀取從客戶端傳來的數據"""
        print("讀取線程已啟動，等待數據...")
        while self.is_running.is_set():
            try:
                result, data = win32file.ReadFile(self.handle, 4096)
                if data:
                    try:
                        received_json = json.loads(data.decode('utf-8').strip())
                        print(f"收到 JSON 數據: {received_json}")
                        print(f"接收時間: {time.time()}")
                        print("-" * 20)
                        time.sleep(1)
                    except (json.JSONDecodeError, UnicodeDecodeError) as e:
                        print(f"收到的數據不是有效的 JSON: {data.decode('utf-8', errors='ignore').strip()}, 錯誤: {e}")

            except pywintypes.error as e:
                if e.winerror in [109, 232]:
                    print("客戶端已斷開連接。")
                else:
                    print(f"讀取時發生未知 win32 錯誤: {e}")
                self.stop()
                break
            except Exception as e:
                print(f"讀取時發生未知錯誤: {e}")
                self.stop()
                break
        print("讀取線程已停止。")

    def writer(self):
        """此線程定時向客戶端發送數據"""
        print("寫入線程已啟動，將定時發送數據...")
        while self.is_running.is_set():
            try:
                data_to_send = {
                    "command": "GET_STATE",
                    "timestamp": time.time()
                }
                json_string = json.dumps(data_to_send)
                msg = f"{json_string}\r\n".encode('utf-8')

                win32file.WriteFile(self.handle, msg)
                time.sleep(2)

            except pywintypes.error as e:
                if e.winerror in [109, 232]:
                    print("客戶端已斷開連接，無法寫入。")
                else:
                    print(f"寫入時發生未知 win32 錯誤: {e}")
                self.stop()
                break
            except Exception as e:
                print(f"寫入時發生未知錯誤: {e}")
                self.stop()
                break
        print("寫入線程已停止。")

    # <<< 主要修正點：將 KeyboardInterrupt 處理移至此方法內部 >>>
    def start(self):
        """啟動伺服器，等待客戶端連接並開始通信"""
        print(f"伺服器啟動，監聽管道 '{self.pipe_name}'... (按下 Ctrl+C 退出)")
        try:
            # 創建一個伺服器主循環，以便在客戶端斷開後可以重新等待連接
            while True:
                self.handle = win32pipe.CreateNamedPipe(
                    self.pipe_name,
                    win32pipe.PIPE_ACCESS_DUPLEX,
                    win32pipe.PIPE_TYPE_MESSAGE | win32pipe.PIPE_READMODE_MESSAGE | win32pipe.PIPE_WAIT,
                    win32pipe.PIPE_UNLIMITED_INSTANCES,
                    65536, 65536,
                    0, None
                )
                print("管道已創建，等待客戶端連接...")
                try:
                    # <<< 這裡的阻塞調用現在被外層的 try...except KeyboardInterrupt 包裹 >>>
                    win32pipe.ConnectNamedPipe(self.handle, None)
                    print("客戶端已連接！")

                    self.is_running.set()

                    self.reader_thread = threading.Thread(target=self.reader, daemon=True)
                    self.writer_thread = threading.Thread(target=self.writer, daemon=True)
                    self.reader_thread.start()
                    self.writer_thread.start()
                    
                    self.is_running.wait()
                    
                    self.reader_thread.join()
                    self.writer_thread.join()
                    print("會話結束，準備接受下一個連接。")

                except pywintypes.error as e:
                    print(f"等待連接時發生錯誤: {e}")
                finally:
                    # 確保在每次連接結束後都關閉句柄
                    if self.handle:
                        win32file.CloseHandle(self.handle)
                        self.handle = None
        
        except KeyboardInterrupt:
            # 當用戶按下 Ctrl+C 時，捕獲異常並跳出 while True 循環
            print("\n收到退出信號，正在關閉伺服器。")
        
        finally:
            self.stop()
            print("伺服器已完全關閉。")


    def stop(self):
        """停止讀寫線程並關閉管道連接"""
        if self.is_running.is_set():
            print("正在停止伺服器會話...")
            self.is_running.clear()
        
        # 即使線程已停止，也要確保關閉句柄
        if self.handle:
            try:
                win32file.CloseHandle(self.handle)
                self.handle = None
            except pywintypes.error as e:
                # 如果句柄已經失效，可能會報錯，可以忽略
                print(f"關閉句柄時出錯（可能已關閉）: {e}")


    def get_isRunning(self) -> bool:
        """檢查伺服器當前是否處於連接和運行狀態"""
        return self.is_running.is_set()

# --- 使用範例 ---
if __name__ == '__main__':
    pipe_name = r'\\.\pipe\HighPerfPipe'
    server = NamePipeServer(pipe_name)
    # 現在直接調用 start() 即可，它內部會處理 Ctrl+C
    server.start()