
using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenTabletDriver.Native.Windows.Input;
using osu.Desktop;
using osu.Framework.Graphics;
using osu.Framework.Input.Handlers;
using osu.Framework.Input.Handlers.Mouse;
using osu.Framework.Screens;
using osu.Game.Input.Handlers;
using osu.Game.Screens.Play;



// API 服務器將在 OsuGameDesktop.cs 中的 LoadComplete 函數中啓動
// 在 OsuGameDesktop.cs 中的 LoadComplete 上方中移除下方兩行代碼

// public new Scheduler Scheduler => base.Scheduler;
// server.Start();

// 以及 LoadComplete 中的下方兩行代碼將復原所有修改

// var server = new ApiServer(this);
// server.Start();


internal class ApiServer : IDisposable
{
    // 修改 #1: 將類型從 OsuGame 改為 OsuGameDesktop
    private readonly OsuGameDesktop game;
    private readonly HttpListener listener = new();
    private CancellationTokenSource? cts;
    private Task? listenTask;
    private readonly string url = "http://localhost:5000/";

    // 修改 #2: 建構函式也使用 OsuGameDesktop
    public ApiServer(OsuGameDesktop game)
    {
        this.game = game;
        listener.Prefixes.Add(url);
    }

    public void Start()
    {
        if (listener.IsListening) return;
        listener.Start();
        cts = new CancellationTokenSource();
        listenTask = Task.Run(() => listenLoopAsync(cts.Token));
        Console.WriteLine("✅ API Server started on " + url);
    }

    private async Task listenLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var ctx = await listener.GetContextAsync().ConfigureAwait(false);
                _ = Task.Run(() => handleRequestAsync(ctx, token), token);
            }
        }
        catch (HttpListenerException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { Console.WriteLine($"[API Server Error] {ex}"); }
    }

    private Task<object> getCurrentStateAsync()
    {
        var tcs = new TaskCompletionSource<object>();

        // 修改 #3: 現在可以安全地呼叫公開的 game.Scheduler.Add()
        game.Scheduler.Add(() =>
        {
            try
            {
                // 修改 #4: 呼叫我們新增的公開方法來獲取畫面
                IScreen currentScreen = game.GetCurrentScreen();

                if (currentScreen is Player player && player.IsLoaded)
                {
                    var state = new
                    {
                        IsInGame = true,
                        //beatmap1 = player.Beatmap.ToString(),
                        //beatmap2 = player.GameplayState.Beatmap.ToString(),
                        //beatmap_HitObj = player.GameplayState.Beatmap.HitObjects.ToString(),
                        


                        HasFailed = player.GameplayState.HasFailed,
                        HasCompleted = player.GameplayState.HasCompleted,

                        HealthAtJudgement = player.GameplayState.LastJudgementResult.Value.HealthAtJudgement,
                        IsHit = player.GameplayState.LastJudgementResult.Value.IsHit,
                        HealthIncrease = player.GameplayState.LastJudgementResult.Value.HealthIncrease,

                        score = player.GameplayState.ScoreProcessor.TotalScore.Value,
                        accuracy = player.GameplayState.ScoreProcessor.Accuracy.Value,
                        combo = player.GameplayState.ScoreProcessor.Combo.Value,
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

    private async Task handleRequestAsync(HttpListenerContext ctx, CancellationToken token)
    {
        var request = ctx.Request;
        var response = ctx.Response;
        var path = request.Url?.AbsolutePath ?? "";
        IScreen currentScreen = game.GetCurrentScreen();

        try
        {
            // 修改 #1: 根據 HTTP 方法和路徑來分發請求
            if (request.HttpMethod == "GET" && path == "/state")
            {
                var state = await getCurrentStateAsync().ConfigureAwait(false);
                var json = JsonSerializer.Serialize(state);
                var buffer = System.Text.Encoding.UTF8.GetBytes(json);
                response.ContentType = "application/json";
                await response.OutputStream.WriteAsync(buffer.AsMemory(), token).ConfigureAwait(false);
            }
            // 新增 #4: 處理 POST request 到 /action 路徑
            else if (request.HttpMethod == "POST" && path == "/action")
            {
                // 從 request body 中讀取 JSON 字串
                using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
                var jsonString = await reader.ReadToEndAsync();

                var action = jsonString

                if (action == null)
                {
                    response.StatusCode = (int)HttpStatusCode.BadRequest; // 400 Bad Request 表示客戶端發送的資料有誤
                }


                if (action.click == true)
                {
                    game.TriggerClick();
                }
                else if (action.move != null)
                {
                    player.MoveTo();
                }
                // 回應成功狀態碼
                response.StatusCode = (int)HttpStatusCode.NoContent; // 204 No Content 通常用於表示成功處理但無需返回內容
            }
            else
            {
                // 如果路徑不匹配，返回 404 Not Found
                response.StatusCode = (int)HttpStatusCode.NotFound;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
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
            cts?.Cancel();
            listener.Stop();
            listenTask?.Wait(1000);
            Console.WriteLine("🛑 API Server stopped.");
        }
        catch { }
    }

    public void Dispose() => Stop();
}
