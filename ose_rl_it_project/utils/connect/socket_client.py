import socket
import json
import time
import sys

# 伺服器設定
HOST = '127.0.0.1'  # 本地回環地址
PORT = 8888         # 必須與 C# 伺服器端設定的通訊埠相同

def main():
    """主函數，用於連接伺服器並進行請求-回應循環"""
    while True:
        try:
            # 建立一個 TCP/IP socket
            with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
                print(f"正在連接到伺服器 {HOST}:{PORT}...")
                sock.connect((HOST, PORT))
                print("已成功連接到伺服器！")

                # 將 socket 物件轉換為檔案物件，方便進行行讀寫
                # 使用 'w' 和 'r' 模式來匹配 C# 的 StreamWriter/Reader
                # buffering=1 表示行緩衝
                write_file = sock.makefile('w', encoding='utf-8', newline='\n', buffering=1)
                read_file = sock.makefile('r', encoding='utf-8', newline='\n')

                # 請求-回應循環
                while True:
                    try:
                        # 1. 發送請求到 C# 伺服器
                        request_message = "get_status"
                        print(f"\n[請求] -> {request_message}")
                        write_file.write(request_message + '\n')
                        # write_file.flush() # 在行緩衝模式下，換行符會自動刷新

                        # 2. 接收來自 C# 伺服器的回應
                        response_json = read_file.readline()

                        # 如果 readline 返回空字串，表示伺服器端已關閉連接
                        if not response_json:
                            print("伺服器已斷開連接。")
                            break

                        # 3. 解析並印出回應
                        response_data = json.loads(response_json)
                        print(f"[回應] <- {json.dumps(response_data, indent=2)}")

                        # 等待 1 秒後再發送下一次請求
                        time.sleep(10)

                    except (ConnectionResetError, BrokenPipeError) as e:
                        print(f"與伺服器的連接中斷: {e}")
                        break
                    except Exception as e:
                        print(f"通信過程中發生錯誤: {e}")
                        break

        except ConnectionRefusedError:
            print("無法連接到伺服器，可能伺服器尚未啟動。10秒後重試...")
            time.sleep(10)
        except Exception as e:
            import traceback
            traceback.print_exc()
            
if __name__ == "__main__":
    main()