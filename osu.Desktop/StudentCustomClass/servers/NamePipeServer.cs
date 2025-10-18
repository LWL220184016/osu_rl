
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Input.Handlers.student;
using osu.Framework.Logging;
using osu.Framework.Screens;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Screens.Play;

namespace osu.Desktop.StudentCustomClass.servers
{
    internal class NamePipeServer : IDisposable
    {
        private readonly OsuGameDesktop game;
        private readonly ApiInputHandler apiInputHandler;
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        private Task? serverTask;

        public NamePipeServer(OsuGameDesktop game, ApiInputHandler handler)
        {
            this.game = game;
            apiInputHandler = handler;

            // 註冊退出事件，當主程式關閉時自動清理
            AppDomain.CurrentDomain.ProcessExit += (_, __) => Dispose();
        }

        public void Start()
        {
            serverTask = runServerAsync(cts.Token);
        }

        private async Task runServerAsync(CancellationToken token)
        {
            Logger.Log("C# Named Pipe Server 啟動中...", LoggingTarget.Input);

            while (!token.IsCancellationRequested)
            {
                NamedPipeServerStream? pipeServer = null;
                try
                {
                    pipeServer = new NamedPipeServerStream(
                        "HighPerfPipe", PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                    Logger.Log("等待 Python 客戶端連接...", LoggingTarget.Input);
                    await pipeServer.WaitForConnectionAsync(token).ConfigureAwait(false);
                    Logger.Log("客戶端已連接！", LoggingTarget.Input);

                    using var reader = new StreamReader(pipeServer, Encoding.UTF8);
                    using var writer = new StreamWriter(pipeServer, Encoding.UTF8) { AutoFlush = true };

                    pipeServer = null; // 轉移所有權，避免 dispose

                    // 啟動讀取循環
                    var readTask = Task.Run(async () =>
                    {
                        while (!token.IsCancellationRequested)
                        {
                            string? line = await reader.ReadLineAsync().ConfigureAwait(false);
                            if (line == null) { break; }

                            Logger.Log($"student: 收到原始字串: {line}", LoggingTarget.Input, LogLevel.Debug);
                            try
                            {
                                using (JsonDocument doc = JsonDocument.Parse(line))
                                {
                                    apiInputHandler.PerformAction(doc);
                                }
                            }
                            catch (JsonException ex)
                            {
                                Logger.Log($"student: JSON 解析失敗: {ex.Message}. 原始字串: \"{line}\"", LoggingTarget.Input, LogLevel.Error);
                            }
                            catch (Exception ex)
                            {
                                // 捕獲其他可能的例外
                                Logger.Log($"student: 處理訊息時發生未預期的錯誤: {ex.Message}", LoggingTarget.Input, LogLevel.Error);
                            }
                        }
                    }, token);

                    // 持續發送遊戲狀態
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            var state = await getCurrentStateAsync().ConfigureAwait(false);
                            string json = JsonSerializer.Serialize(state);
                            await writer.WriteLineAsync(json).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"發送狀態時出錯: {ex.Message}", LoggingTarget.Input);
                            break; // 出錯時跳出，重新連接
                        }

                        await Task.Delay(500, token).ConfigureAwait(false); // 每 50ms 發送一次 (20Hz)，可自行調整
                    }

                    await readTask; // 等待讀取任務結束
                }
                catch (OperationCanceledException)
                {
                    // 正常取消
                    break;
                }
                catch (IOException ex)
                {
                    Logger.Log($"管道 IO 錯誤 (斷線或客戶端離開): {ex.Message}，等待重連...", LoggingTarget.Input);
                }
                catch (Exception ex)
                {
                    Logger.Log($"伺服器錯誤: {ex.Message}，等待重連...", LoggingTarget.Input);
                }
                finally
                {
                    pipeServer?.Dispose();
                }

                // 斷線後短暫延遲後重試，避免 CPU 過載
                if (!token.IsCancellationRequested)
                {
                    await Task.Delay(1000, token).ConfigureAwait(false);
                }
            }

            Logger.Log("Named Pipe Server 已停止。", LoggingTarget.Input);
        }


        private Task<object> getCurrentStateAsync()
        {
            var tcs = new TaskCompletionSource<object>();

            game.Scheduler.Add(() =>
            {
                try
                {
                    var currentScreen = game.GetCurrentScreen();

                    if (currentScreen is Player player && player.IsLoaded)
                    {
                        var gameplay = player.GameplayState;

                        // 預設值，避免 null
                        bool? hasFailed = null;
                        bool? hasCompleted = null;
                        double? healthAtJudgement = null;
                        bool? isHit = null;
                        double? healthIncrease = null;
                        long? score = null;
                        double? accuracy = null;
                        int? combo = null;


                        if (gameplay != null)
                        {
                            hasFailed = gameplay.HasFailed;
                            hasCompleted = gameplay.HasCompleted;

                            if (gameplay.LastJudgementResult.Value is not null) 
                            {
                                var jr = gameplay.LastJudgementResult.Value;
                                healthAtJudgement = jr.HealthAtJudgement;
                                isHit = jr.IsHit;
                                healthIncrease = jr.HealthIncrease;
                            }

                            if (gameplay.ScoreProcessor is not null)
                            {
                                score = gameplay.ScoreProcessor.TotalScore.Value;
                                accuracy = gameplay.ScoreProcessor.Accuracy.Value;
                                combo = gameplay.ScoreProcessor.Combo.Value;
                            }

                            //if (gameplay.Beatmap is not null)
                            //{
                                //如果要去拿打擊點的坐標，可能要到 drawable 的子類，或者渲染邏輯裏面找
                            //}
                        }

                        var state = new
                        {
                            IsInGame = true,
                            HasFailed = hasFailed,
                            HasCompleted = hasCompleted,
                            HealthAtJudgement = healthAtJudgement,
                            IsHit = isHit,
                            HealthIncrease = healthIncrease,
                            score,
                            accuracy,
                            combo,
                        };

                        tcs.SetResult(state);
                    }
                    else
                    {
                        var state = new
                        {
                            IsInGame = false,
                            Time = game.Clock.CurrentTime,
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

        public void Dispose()
        {
            if (!cts.IsCancellationRequested)
            {
                Logger.Log("正在清理 Named Pipe Server...", LoggingTarget.Input);
                cts.Cancel();
            }
            serverTask?.Wait(1000); // 等待任務結束
            cts.Dispose();
        }
    }
}
