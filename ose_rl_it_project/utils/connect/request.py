# import requests
# import json

# def request_get():
#     url = "http://localhost:5000/state"

#     print(f"發送 GET 請求到: {url}")

#     try:
#         # 發送 GET 請求
#         response = requests.get(url)

#         if response.status_code == 200:
#             print("✅ 請求成功！")
#             data = response.json()
            
#             print("伺服器回應 (JSON):")
#             print(json.dumps(data, indent=4, ensure_ascii=False))
            
#             return data
#         else:
#             print(f"❌ 請求失敗，狀態碼: {response.status_code}")
#             print("回應內容:", response.text)

#     except Exception:
#         import traceback
#         traceback.print_exc()

# def request_post(data):

#     url = "http://localhost:5000/action"

#     # 設定請求標頭 (headers)，指定我們發送的是 JSON 格式的資料
#     # 雖然 requests 會經常自動處理，但明確指定是個好習慣
#     headers = {
#         'Content-Type': 'application/json; charset=utf-8'
#     }

#     print(f"發送 POST 請求到: {url}")
#     print(f"傳送的資料: {data}")

#     try:
#         response = requests.post(url, json=data, headers=headers)

#         # 檢查 HTTP 狀態碼是否為 201 (Created)，這是 POST 成功新增資源的標準狀態碼
#         # response.raise_for_status() # 同樣，如果狀態碼不是 2xx 會拋出異常

#         if response.status_code >= 200 and response.status_code <= 250:
#             print("✅ 請求成功，資源已建立！")
            
#             # 伺服器通常會回傳剛剛建立的資源，並加上一個 id
#             created_post = response.json()
            
#             print("伺服器回應 (JSON):")
#             print(json.dumps(created_post, indent=4, ensure_ascii=False))

#         else:
#             print(f"❌ 請求失敗，狀態碼: {response.status_code}")
#             print("回應內容:", response.text)

#     except requests.exceptions.RequestException as e:
#         # 處理網路連線錯誤、超時等問題
#         print(f"發生錯誤: {e}")