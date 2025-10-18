# 專為桌面版修改的腳本，用於使用本地的 osu-framework。
# 執行前，請確保 osu-framework 資料夾與 osu 資料夾位於同一層級。

$GAME_CSPROJ="osu.Game/osu.Game.csproj"
$SLN="osu.sln"
$DESKTOP_SLNF="osu.Desktop.slnf"

# 1. 從遊戲專案中移除對官方框架 NuGet 套件的引用
dotnet remove $GAME_CSPROJ reference ppy.osu.Framework

# 2. 將本地的框架專案加入到主解決方案中
dotnet sln $SLN add ../osu-framework/osu.Framework/osu.Framework.csproj `
    ../osu-framework/osu.Framework.NativeLibs/osu.Framework.NativeLibs.csproj

# 3. 將遊戲專案的引用指向本地的框架專案
dotnet add $GAME_CSPROJ reference ../osu-framework/osu.Framework/osu.Framework.csproj

# 4. 更新桌面版的解決方案過濾器 (solution filter)，以便在 Visual Studio 中能看到框架專案
$TMP=New-TemporaryFile
$SLNF_CONTENT=Get-Content $DESKTOP_SLNF | ConvertFrom-Json
$SLNF_CONTENT.solution.projects += ("../osu-framework/osu.Framework/osu.Framework.csproj", "../osu-framework/osu.Framework.NativeLibs/osu.Framework.NativeLibs.csproj")
ConvertTo-Json $SLNF_CONTENT | Out-File $TMP -Encoding UTF8
Move-Item -Path $TMP -Destination $DESKTOP_SLNF -Force

Write-Host "成功將 osu! lazer 的框架切換為您的本地版本 (僅桌面版)。"