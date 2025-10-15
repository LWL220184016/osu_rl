
using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using osu.Desktop;
using osu.Framework.Screens;
using osu.Game.Screens.Play;
using osu.Framework.Input.Handlers.Mouse;
using osuTK.Input;


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

    private async Task sendSuccessResponse(HttpListenerResponse response, string originalJsonData, bool actionStatus)
    {
        try
        {
            // 1. 將原始 JSON 字串解析為 JsonObject
            //    我們假設傳入的 originalJsonData 必然是個有效的 JSON 物件，因為它在之前已經被驗證過了
            var jsonObject = System.Text.Json.Nodes.JsonNode.Parse(originalJsonData)!.AsObject();

            // 2. 新增或更新 'actionStatus' 屬性
            jsonObject["actionStatus"] = actionStatus;

            // 3. 將修改後的物件序列化回字串
            string newJsonPayload = jsonObject.ToJsonString();

            // 4. 傳送新的 JSON payload
            response.StatusCode = (int)HttpStatusCode.OK; // 200 OK
            response.ContentType = "application/json";
            var buffer = System.Text.Encoding.UTF8.GetBytes(newJsonPayload);
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }
        catch (Exception ex)
        {
            // 如果在處理成功回應時發生意外（例如 JSON 解析失敗），則回傳內部伺服器錯誤
            await sendErrorResponse(response, HttpStatusCode.InternalServerError, $"建立成功回應時發生錯誤: {ex.Message}");
        }
    }

    private async Task sendErrorResponse(HttpListenerResponse response, HttpStatusCode statusCode, string message)
    {
        response.StatusCode = (int)statusCode;
        response.ContentType = "application/json";
        // 建立一個包含錯誤訊息的 JSON 物件
        var errorPayload = JsonSerializer.Serialize(new { error = message });
        var buffer = System.Text.Encoding.UTF8.GetBytes(errorPayload);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
    }

    private async Task handleRequestAsync(HttpListenerContext ctx, CancellationToken token)
    {
        var request = ctx.Request;
        var response = ctx.Response;
        var path = request.Url?.AbsolutePath ?? "";


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
                IScreen currentScreen = game.GetCurrentScreen();
                if (!(currentScreen is Player player && player.IsLoaded))
                {
                    await sendErrorResponse(response, HttpStatusCode.Conflict, "遊戲尚未載入或目前畫面不接受操作。");
                    return;
                }

                using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
                var jsonString = await reader.ReadToEndAsync();
                bool actionStatus = false;

                if (string.IsNullOrWhiteSpace(jsonString))
                {
                    await sendErrorResponse(response, HttpStatusCode.BadRequest, "請求內文 (Request body) 不得為空。");
                    return;
                }

                try
                {
                    var actionNode = System.Text.Json.Nodes.JsonNode.Parse(jsonString);
                    var actionObject = actionNode as System.Text.Json.Nodes.JsonObject;

                    if (actionObject == null)
                    {
                        await sendErrorResponse(response, HttpStatusCode.BadRequest, "傳入的 JSON 格式必須是一個物件。");
                        return;
                    }

                    bool actionHandled = false;

                    // 檢查 'click' 屬性
                    if (actionObject.TryGetPropertyValue("click", out var clickNode))
                    {
                        if (clickNode != null && clickNode.GetValue<JsonElement>().ValueKind == JsonValueKind.True)
                        {
                            actionStatus = game.TriggerClick();
                            actionHandled = true;
                        }
                    }
                    // 檢查 'move' 物件
                    else if (actionObject.TryGetPropertyValue("move", out var moveNode))
                    {
                        var moveObject = moveNode as System.Text.Json.Nodes.JsonObject;
                        if (moveObject == null)
                        {
                            await sendErrorResponse(response, HttpStatusCode.BadRequest, "'move' 屬性的值必須是一個包含 x 和 y 的物件。");
                            return;
                        }

                        if (moveObject.TryGetPropertyValue("x", out var xNode) && xNode.GetValue<JsonElement>().TryGetInt32(out int x) &&
                            moveObject.TryGetPropertyValue("y", out var yNode) && yNode.GetValue<JsonElement>().TryGetInt32(out int y))
                        {
                            // player.MoveTo(new osuTK.Vector2(x, y));
                            actionHandled = true;
                        }
                        else
                        {
                            await sendErrorResponse(response, HttpStatusCode.BadRequest, "'move' 物件必須包含型別為整數的 'x' 和 'y' 屬性。");
                            return;
                        }
                    }

                    // *** 修改部分 ***
                    // 如果有任何動作被成功處理
                    if (actionHandled)
                    {
                        // 回傳 200 OK 並附上使用者傳入的原始 JSON 資料
                        await sendSuccessResponse(response, jsonString, actionStatus);
                    }
                    else
                    {
                        // 如果請求有效但沒有可執行的動作，回傳錯誤
                        await sendErrorResponse(response, HttpStatusCode.BadRequest, "請求中未包含有效的 'click' 或 'move' 動作。");
                    }
                }
                catch (JsonException ex)
                {
                    await sendErrorResponse(response, HttpStatusCode.BadRequest, $"JSON 格式錯誤: {ex.Message}");
                }
            }
            else
            {
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
