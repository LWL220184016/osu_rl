# 需要注意
  這是一個經過修改用於强化學習訓練的 osu! lazer 客戶端，已經 Android，iOS 以及除了標準模式外的所有 rulesets


## 使用步驟
1. 安裝好 visual studio 免費版以及 .net 8

2. 在一個空的文件夾中打開 cmd 或者 powershell 并輸入以下三個指令：
    git clone https://github.com/LWL220184016/osu_rl.git
    git clone https://github.com/LWL220184016/osu-framework.git
    git clone https://github.com/LWL220184016/ose_rl_script.git

    建議把文件夾名字更改為：osu_rl -> client, osu-framework -> framework

3. 打開存放 osu.sln 的文件夾然後打開 cmd 或者 powershell 

4. 在 powershell 中輸入以下指令: 
   dotnet publish osu.Desktop\osu.Desktop.csproj -c Release -r win-x64 --self-contained true

5. 建議按照下方創建文件夾 (多少個 worker 文件取決於你想啓動多少并行環境。不過如果你只想啓動一個訓練環境，rllib 也可能需要額外啓動一個測試環境)：
    osu_rl_release --- worker_0
                 | --- worker_1
                 | --- worker_2
                 ...

6. 在存放 osu.sln 的文件夾中，前往路徑 osu.Desktop\bin\Release\netX.X\win-x64\publish\ 并且創建一個新的文件 framework.ini 

7. 創建新的文件 framework.ini 後複製裏面的所有文件到你創建的每一個 worker 文件夾内

8. 

