
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Input.Handlers.student;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Framework.Threading;
using osu.Game.Beatmaps;
using osu.Game.Screens.Menu;
using osu.Game.Screens.Play;
using osu.Game.Screens.SelectV2;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace osu.Desktop.StudentCustomClass.connections
{
    internal class NamePipeClient : IDisposable
    {
        private readonly OsuGameDesktop game;
        private readonly ApiInputHandler apiInputHandler;
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        private Task? serverTask;
        private GameHost host;
        private string modelObsType;
        private int imageX;
        private int imageY;
        private int delay;
        private string workerID = "0";

        public NamePipeClient(OsuGameDesktop game, ApiInputHandler handler, GameHost host)
        {
            this.game = game;
            apiInputHandler = handler;
            this.host = host;
            AppDomain.CurrentDomain.ProcessExit += (_, __) => Dispose();
        }

        public void Start(string WorkerID)
        {
            if (WorkerID != null)
            {
                workerID = WorkerID;
            }
            serverTask = runServerAsync(cts.Token);
        }

        private async Task runServerAsync(CancellationToken token)
        {
            Logger.Log("student: C# Named Pipe Server 啟動中...", LoggingTarget.Input);

            while (!token.IsCancellationRequested)
            {
                NamedPipeClientStream? pipeClient = null;
                try
                {
                    pipeClient = new NamedPipeClientStream(
                        ".", // "." 表示本機
                        "HighPerfPipe" + workerID,
                        PipeDirection.InOut,
                        PipeOptions.Asynchronous
                    );

                    Logger.Log($"student: 正在連接到管道 '{"HighPerfPipe" + workerID}'...", LoggingTarget.Input);

                    // 使用 ConnectAsync 來嘗試連接到伺服器，可以設定一個逾時時間
                    // 這裡設定 5 秒逾時
                    await pipeClient.ConnectAsync(5000, token).ConfigureAwait(false);
                    Logger.Log("student: 已連接到服務器！", LoggingTarget.Input);

                    // 建立讀取與發送任務
                    var sendTask = Task.Run(() => sendLoopAsync(pipeClient, token));
                    var recvTask = Task.Run(() => recvLoopAsync(pipeClient, token));

                    await Task.WhenAny(sendTask, recvTask).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException ex)
                {
                    Logger.Log($"student: 管道 IO 錯誤 (斷線或客戶端離開): {ex.Message}，等待重連...", LoggingTarget.Input, LogLevel.Error);
                }
                catch (Exception ex)
                {
                    Logger.Log($"student: 伺服器錯誤: {ex.Message}，等待重連...", LoggingTarget.Input, LogLevel.Error);
                }
                finally
                {
                    pipeClient?.Dispose();
                }

                if (!token.IsCancellationRequested)
                {
                    await Task.Delay(1000, token).ConfigureAwait(false);
                }
            }

            Logger.Log("student: Named Pipe Server 已停止。", LoggingTarget.Input);
        }

        // 傳送遊戲狀態
        private async Task sendLoopAsync(NamedPipeClientStream pipe, CancellationToken token)
        {
            while (!token.IsCancellationRequested && pipe.IsConnected)
            {
                try
                {
                    var (metaJson, imageBytes) = await getCurrentStateAsync().ConfigureAwait(false);
                    byte[] metaBytes = Encoding.UTF8.GetBytes(metaJson);
                    int metaLen = metaBytes.Length;
                    int imgLen = imageBytes?.Length ?? 0;

                    byte[] header = new byte[8];
                    BitConverter.GetBytes(metaLen).CopyTo(header, 0);
                    BitConverter.GetBytes(imgLen).CopyTo(header, 4);

                    await pipe.WriteAsync(header, 0, 8, token).ConfigureAwait(false);
                    await pipe.WriteAsync(metaBytes, 0, metaLen, token).ConfigureAwait(false);
                    if (imgLen > 0)
                        await pipe.WriteAsync(imageBytes, 0, imgLen, token).ConfigureAwait(false);

                    await pipe.FlushAsync(token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logger.Log($"student: 傳輸封包時出錯: {ex.Message}", LoggingTarget.Input, LogLevel.Error);
                    break;
                }

                await Task.Delay(delay, token).ConfigureAwait(false); // FPS 的值等於 1000 除以 delay
            }
        }

        // 讀取 Python 發送的數據
        private async Task recvLoopAsync(NamedPipeClientStream pipe, CancellationToken token)
        {
            byte[] buffer = new byte[4096];
            MemoryStream ms = new MemoryStream();

            while (!token.IsCancellationRequested && pipe.IsConnected)
            {
                try
                {
                    int bytesRead = await pipe.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                    if (bytesRead == 0)
                    {
                        await Task.Delay(5, token).ConfigureAwait(false);
                        continue;
                    }

                    ms.Write(buffer, 0, bytesRead);

                    while (true)
                    {
                        ms.Position = 0;
                        using var reader = new StreamReader(ms, Encoding.UTF8, false, 1024, true);
                        string? line = await reader.ReadLineAsync();
                        if (line == null)
                            break;

                        using (JsonDocument doc = JsonDocument.Parse(line))
                        {
                            var root = doc.RootElement;
                            if (root.TryGetProperty("init_param", out var init_param))
                            {
                                initSetup(init_param);
                            }

                            apiInputHandler.PerformAction(root);
                        }

                        // 剩餘未處理資料複製回新流
                        var remaining = ms.Length - ms.Position;
                        if (remaining > 0)
                        {
                            byte[] tmp = new byte[remaining];
                            ms.Read(tmp, 0, (int)remaining);
                            ms = new MemoryStream(tmp);
                        }
                        else
                        {
                            ms.SetLength(0);
                            break;
                        }
                    }
                }
                catch (IOException)
                {
                    Logger.Log("student: 動作接收端斷開連線。", LoggingTarget.Input, LogLevel.Error);
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Log($"student: recvLoop 錯誤: {ex.Message}", LoggingTarget.Input, LogLevel.Error);
                    ms.SetLength(0);
                }
            }
        }

        // 游戲畫面截圖 + 狀態收集
        private Task<(string metaJson, byte[]? imageBytes)> getCurrentStateAsync()
        {
            var tcs = new TaskCompletionSource<(string, byte[]?)>();

            game.Scheduler.Add(async () =>
            {
                try
                {
                    var currentScreen = game.GetCurrentScreen();

                    byte[]? imageBytes = null;

                    if (currentScreen is Player player && player.IsLoaded)
                    {
                        try
                        {
                            using (Image<Rgba32>? screenshot = await host.TakeScreenshotAsync())
                            {
                                if (screenshot != null)
                                {
                                    screenshot.Mutate(x => x.Resize(new ResizeOptions
                                    {
                                        Size = new(imageX, imageY),
                                        Mode = ResizeMode.Stretch
                                    }));

                                    await using var ms = new MemoryStream();
                                    await screenshot.SaveAsJpegAsync(ms, new JpegEncoder { Quality = 70 });
                                    imageBytes = ms.ToArray();
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"student: 截圖或編碼失敗: {ex.Message}", LoggingTarget.Input, LogLevel.Error);
                        }

                        var gameplay = player.GameplayState;
                        var state = new
                        {
                            IsInGame = true,
                            gameplay?.HasFailed,
                            gameplay?.HasCompleted,

                            gameplay?.LastJudgementResult.Value?.HealthAtJudgement,
                            gameplay?.LastJudgementResult.Value?.IsHit,
                            gameplay?.LastJudgementResult.Value?.HealthIncrease,

                            TotalScore = gameplay?.ScoreProcessor?.TotalScore.Value,
                            Accuracy = gameplay?.ScoreProcessor?.Accuracy.Value,
                            Combo = gameplay?.ScoreProcessor?.Combo.Value
                        };

                        string json = JsonSerializer.Serialize(state);
                        tcs.SetResult((json, imageBytes));
                    }
                    else
                    {
                        string json = JsonSerializer.Serialize(new
                        {
                            IsInGame = false,
                            Time = game.Clock.CurrentTime
                        });
                        tcs.SetResult((json, null));
                    }
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            return tcs.Task;
        }

        private async void initSetup(JsonElement initParam)
        {
            Logger.Log("student: initSetup", LoggingTarget.Input);

            // update obs type for model
            if (initParam.TryGetProperty("model_obs_type", out var model_obs_type))
            {
                modelObsType = model_obs_type.GetString();
                Logger.Log($"student: modelObsType 已更新，modelObsType: {modelObsType}", LoggingTarget.Input);
            }

            // update image size for resize image
            if (initParam.TryGetProperty("image_size", out var image_size))
            {
                if (image_size[0].TryGetInt32(out int x) && image_size[1].TryGetInt32(out int y))
                {
                    imageX = x;
                    imageY = y;
                    Logger.Log($"student: imageSize 已更新，imageX: {imageX}, imageY: {imageY}", LoggingTarget.Input);
                }
                else
                {
                    Logger.Log($"student: image_size JsonElement 转换 int 失敗: {image_size}", LoggingTarget.Input, LogLevel.Error);
                }
            }

            // update delay after send data to python client
            if (initParam.TryGetProperty("fps", out var fps))
            {
                if (fps.TryGetInt32(out int FPS))
                {
                    delay = 1000 / FPS;
                    Logger.Log($"student: delay 已更新，delay: {delay}", LoggingTarget.Input);
                }
                else
                {
                    Logger.Log($"student: fps JsonElement 转换 int 失敗: {fps}", LoggingTarget.Input, LogLevel.Error);
                }
            }


            if (initParam.TryGetProperty("beat_map", out var beat_map))
            {
                IScreen currentScreen = game.GetCurrentScreen();
                string jsonString; JsonElement actionObject;
                bool is_use_esc = false;
                int delay = 500;

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                Dictionary<string, object> beatMapDict = beat_map.Deserialize<Dictionary<string, object>>(options);

                Logger.Log($"Beatmap name from Dict: {beatMapDict["name"]}", LoggingTarget.Input);
                Logger.Log($"Difficulty from Dict: {beatMapDict["difficulty"]}", LoggingTarget.Input);

                var beatmap_name = beatMapDict["name"].ToString();
                var difficulty = beatMapDict["difficulty"];

                while (currentScreen is not SoloPlayer)
                {
                    currentScreen = game.GetCurrentScreen();
                    if (currentScreen is SoloSongSelect soloSongSelect)
                    {
                        soloSongSelect.filterControl.updateCriteria(beatmap_name);

                        while (currentScreen is SoloSongSelect)
                        {
                            Logger.Log($"initSetup enter, enter, enter", LoggingTarget.Input);

                            await SimulateKeyPressAsync("KeyBoard_enter", delay).ConfigureAwait(true);
                            currentScreen = game.GetCurrentScreen();
                        }

                        break;
                    }
                    else
                    {
                        await SimulateKeyPressAsync("KeyBoard_space", delay).ConfigureAwait(true);

                        if (!is_use_esc)
                        {
                            await SimulateKeyPressAsync("KeyBoard_esc", delay).ConfigureAwait(true);
                            await SimulateKeyPressAsync("KeyBoard_esc", delay).ConfigureAwait(true);
                            is_use_esc = true;
                        }
                    }
                }
            }
        }

        private async Task SimulateKeyPressAsync(string keyName, int postPressDelay = 100)
        {
            // 按下
            string downJson = $"{{\"{keyName}\": 0}}";
            apiInputHandler.PerformAction(JsonSerializer.Deserialize<JsonElement>(downJson));

            // 釋放
            string upJson = $"{{\"{keyName}\": 1}}";
            apiInputHandler.PerformAction(JsonSerializer.Deserialize<JsonElement>(upJson));

            // 等待 UI 反應
            await Task.Delay(postPressDelay);
        }

        public void Dispose()
        {
            if (!cts.IsCancellationRequested)
            {
                Logger.Log("student: 正在清理 Named Pipe Server...", LoggingTarget.Input);
                cts.Cancel();
            }
            serverTask?.Wait(1000);
            cts.Dispose();
        }
    }
}
