using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using osu.Desktop;
using osu.Framework.Input;
using osu.Framework.Input.Handlers;
using osu.Framework.Screens;
using osu.Framework.Threading;
using osu.Game.Screens.Play;
using OpenTK.Mathematics;
using System.Collections.Generic;
using osu.Framework.Input.Events;

// 新增 using，用於 ManualInputHandler 和 Vector2
// 請確保你的專案引用了 OpenTK.Mathematics

// API 服務器將在 OsuGameDesktop.cs 中的 LoadComplete 函數中啓動
// 在 OsuGameDesktop.cs 中的 LoadComplete 上方中移除下方兩行代碼

// public new Scheduler Scheduler => base.Scheduler;
// server.Start();

// 以及 LoadComplete 中的下方兩行代碼將復原所有修改

// var server = new ApiServer(this);
// server.Start();


/// <summary>
/// 新增：用於反序列化 POST request body 的資料結構。
/// 代表 AI 想要執行的單一幀的動作。
/// </summary>
internal class PlayerAction
{
    // JsonPropertyName 用於確保 JSON 欄位能正確對應到 C# 屬性
    [JsonPropertyName("x")]
    public float X { get; set; }

    [JsonPropertyName("y")]
    public float Y { get; set; }

    // osu! 有兩個主要按鍵 M1/M2 (滑鼠) 或 K1/K2 (鍵盤)
    [JsonPropertyName("keys")]
    public List<string> Keys { get; set; } = new List<string>();
}

internal class ApiServer : IDisposable
{
    private readonly OsuGameDesktop game;
    private readonly HttpListener listener = new();
    private CancellationTokenSource? cts;
    private Task? listenTask;
    private readonly string url = "http://localhost:5000/";

    // 新增 #1: 手動輸入處理器，用來模擬滑鼠和鍵盤輸入
    private readonly ManualInputHandler inputHandler;

    public ApiServer(OsuGameDesktop game)
    {
        this.game = game;
        listener.Prefixes.Add(url);

        // 新增 #2: 在伺服器初始化時，創建並註冊 ManualInputHandler
        // 我們需要透過 game.Scheduler 來確保這段代碼在遊戲主執行緒上運行
        game.Scheduler.Add(() =>
        {
            // 從遊戲的依賴注入容器中獲取 InputManager
            var inputManager = game.Host.Dependencies.Get<InputManager>();
            // 創建 ManualInputHandler
            inputHandler = new ManualInputHandler();
            // 將我們的 handler 新增到 InputManager 中
            inputManager.AddHandler(inputHandler);
        });
    }

    public void Start()
    {
        if (listener.IsListening) return;
        listener.Start();
        cts = new CancellationTokenSource();
        listenTask = Task.Run(() => ListenLoopAsync(cts.Token));
        Console.WriteLine("✅ API Server started on " + url);
    }

    private async Task ListenLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var ctx = await listener.GetContextAsync().ConfigureAwait(false);
                _ = Task.Run(() => HandleRequestAsync(ctx, token), token);
            }
        }
        catch (HttpListenerException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { Console.WriteLine($"[API Server Error] {ex}"); }
    }

    private Task<object> GetCurrentStateAsync()
    {
        var tcs = new TaskCompletionSource<object>();

        game.Scheduler.Add(() =>
        {
            try
            {
                IScreen currentScreen = game.GetCurrentScreen();

                if (currentScreen is Player player && player.IsLoaded)
                {
                    var state = new
                    {
                        IsInGame = true,
                        //beatmap1 = player.Beatmap.ToString(),
                        //beatmap2 = player.GameplayState.Beatmap.ToString(),
                        //beatmap_HitObj = player.GameplayState.Beatmap.HitObjects.ToString(),


                        HasFailed =    player.GameplayState.HasFailed,
                        HasCompleted = player.GameplayState.HasCompleted,

                        HealthAtJudgement = player.GameplayState.LastJudgementResult.Value.HealthAtJudgement,
                        IsHit =             player.GameplayState.LastJudgementResult.Value.IsHit,
                        HealthIncrease =    player.GameplayState.LastJudgementResult.Value.HealthIncrease,

                        score =    player.GameplayState.ScoreProcessor.TotalScore.Value,
                        accuracy = player.GameplayState.ScoreProcessor.Accuracy.Value,
                        combo =    player.GameplayState.ScoreProcessor.Combo.Value,
                    };
                    tcs.SetResult(state);
                }
                else
                {
                    var state = new
                    {
                        IsInGame = false,
                        Time = game.Clock.CurrentTime
                    };
                    tcs.SetResult(state);
                }
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        return tcs.Task;
    }

    /// <summary>
    /// 新增 #3: 處理來自 AI 的動作指令
    /// </summary>
    private Task HandlePlayerActionAsync(PlayerAction action)
    {
        // 同樣地，所有與遊戲物件的互動都必須在主執行緒上進行
        game.Scheduler.Add(() =>
        {
            // 1. 移動滑鼠到指定位置
            // osu! 的座標系統是 Vector2，我們從 action 中獲取 X 和 Y
            inputHandler.MoveMouseTo(new Vector2(action.X, action.Y));

            // 2. 處理按鍵狀態
            // 為了簡化，我們假設只有兩個按鍵：M1 和 M2 (對應滑鼠左右鍵)
            // 檢查 action.Keys 是否包含 "M1"
            bool m1Pressed = action.Keys.Contains("M1");
            bool m2Pressed = action.Keys.Contains("M2");

            // 獲取當前按鍵狀態
            var currentState = inputHandler.CurrentState;
            bool isM1CurrentlyPressed = currentState.Mouse.IsPressed(MouseButton.Left);
            bool isM2CurrentlyPressed = currentState.Mouse.IsPressed(MouseButton.Right);

            // 根據需要按下或釋放按鍵
            if (m1Pressed && !isM1CurrentlyPressed)
                inputHandler.PressMouseButton(MouseButton.Left);
            else if (!m1Pressed && isM1CurrentlyPressed)
                inputHandler.ReleaseMouseButton(MouseButton.Left);

            if (m2Pressed && !isM2CurrentlyPressed)
                inputHandler.PressMouseButton(MouseButton.Right);
            else if (!m2Pressed && isM2CurrentlyPressed)
                inputHandler.ReleaseMouseButton(MouseButton.Right);
        });

        return Task.CompletedTask;
    }

    private async Task HandleRequestAsync(HttpListenerContext ctx, CancellationToken token)
    {
        var request = ctx.Request;
        var response = ctx.Response;
        var path = request.Url?.AbsolutePath ?? "";

        try
        {
            // 修改 #1: 根據 HTTP 方法和路徑來分發請求
            if (request.HttpMethod == "GET" && path == "/state")
            {
                var state = await GetCurrentStateAsync().ConfigureAwait(false);
                var json = JsonSerializer.Serialize(state);
                var buffer = System.Text.Encoding.UTF8.GetBytes(json);
                response.ContentType = "application/json";
                response.StatusCode = (int)HttpStatusCode.OK;
                await response.OutputStream.WriteAsync(buffer.AsMemory(), token).ConfigureAwait(false);
            }
            // 新增 #4: 處理 POST request 到 /action 路徑
            else if (request.HttpMethod == "POST" && path == "/action")
            {
                // 從 request body 中讀取 JSON 字串
                using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
                var jsonString = await reader.ReadToEndAsync();

                // 將 JSON 反序列化為 PlayerAction 物件
                var action = JsonSerializer.Deserialize<PlayerAction>(jsonString);

                if (action != null)
                {
                    // 執行玩家動作
                    await HandlePlayerActionAsync(action);
                    // 回應成功狀態碼
                    response.StatusCode = (int)HttpStatusCode.NoContent; // 204 No Content 通常用於表示成功處理但無需返回內容
                }
                else
                {
                    response.StatusCode = (int)HttpStatusCode.BadRequest; // 400 Bad Request 表示客戶端發送的資料有誤
                }
            }
            else
            {
                // 如果路徑不匹配，返回 404 Not Found
                response.StatusCode = (int)HttpStatusCode.NotFound;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HandleRequest Error] {ex.Message}");
            if (!response.HeadersSent)
            {
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
            }
        }
        finally
        {
            response.Close();
        }
    }

    public void Stop()
    {
        try
        {
            // 新增 #5: 在伺服器停止時，也從遊戲中移除我們的 input handler
            game.Scheduler.Add(() =>
            {
                var inputManager = game.Host.Dependencies.Get<InputManager>();
                inputManager.RemoveHandler(inputHandler);
            });
            
            cts?.Cancel();
            listener.Stop();
            listenTask?.Wait(1000);
            Console.WriteLine("🛑 API Server stopped.");
        }
        catch { }
    }

    public void Dispose() => Stop();
}