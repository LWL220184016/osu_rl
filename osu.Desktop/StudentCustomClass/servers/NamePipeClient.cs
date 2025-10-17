
//using System;
//using System.IO;
//using System.IO.Pipes;
//using System.Text;
//using System.Text.Json;
//using System.Threading;
//using System.Threading.Tasks;
//using osu.Framework.Logging;
//using osu.Framework.Screens;
//using osu.Game.Screens.Play;

//namespace osu.Desktop.StudentCustomClass.servers
//{
//    internal class NamePipeClient : IDisposable
//    {
//        private readonly OsuGameDesktop game;
//        private readonly ApiInputHandler apiInputHandler;
//        private readonly CancellationTokenSource cts = new CancellationTokenSource();
//        private Task? serverTask;
//        private int num_msg = 0;

//        public NamePipeClient(OsuGameDesktop game, ApiInputHandler handler)
//        {
//            this.game = game;
//            apiInputHandler = handler;

//            // 註冊退出事件，當主程式關閉時自動清理
//            AppDomain.CurrentDomain.ProcessExit += (_, __) => Dispose();
//        }

//        public void Start()
//        {
//            serverTask = runServerAsync(cts.Token);
//        }

//        private async Task runServerAsync(CancellationToken token)
//        {
//            Logger.Log("C# Named Pipe Client 啟動中...", LoggingTarget.Input);

//            while (!token.IsCancellationRequested)
//            {
//                NamedPipeClientStream? pipeClient = null;
//                try
//                {
//                    pipeClient = new NamedPipeClientStream(
//                        ".", "HighPerfPipe", PipeDirection.InOut,
//                        PipeOptions.Asynchronous);

//                    Logger.Log("嘗試連接伺服器...", LoggingTarget.Input);
//                    await pipeClient.ConnectAsync(5000, token).ConfigureAwait(false); // 最多等 5 秒
//                    Logger.Log("已連接到伺服器！", LoggingTarget.Input);

//                    using var reader = new StreamReader(pipeClient, Encoding.UTF8);
//                    using var writer = new StreamWriter(pipeClient, Encoding.UTF8) { AutoFlush = true };

//                    pipeClient = null; // 轉移所有權，避免 dispose

//                    // 啟動讀取循環
//                    var readTask = Task.Run(async () =>
//                    {
//                        while (!token.IsCancellationRequested)
//                        {
//                            string? line = await reader.ReadLineAsync().ConfigureAwait(false);
//                            if (line == null) break;
//                            Logger.Log($"student: 收到: {line}", LoggingTarget.Input, LogLevel.Important);

//                            var state = await getCurrentStateAsync().ConfigureAwait(false);
//                            string json = JsonSerializer.Serialize(state);
//                            await writer.WriteLineAsync(json).ConfigureAwait(false);
//                        }
//                    }, token);

//                    //// 持續發送遊戲狀態
//                    //while (!token.IsCancellationRequested)
//                    //{
//                    //    try
//                    //    {
                            
//                    //    }
//                    //    catch (Exception ex)
//                    //    {
//                    //        Logger.Log($"發送狀態時出錯: {ex.Message}", LoggingTarget.Input);
//                    //        break; // 出錯時跳出，重新連接
//                    //    }

//                    //    await Task.Delay(50, token).ConfigureAwait(false); // 每 50ms 發送一次 (20Hz)，可自行調整
//                    //}

//                    await readTask; // 等待讀取任務結束
//                }
//                catch (OperationCanceledException)
//                {
//                    // 正常取消
//                    break;
//                }
//                catch (IOException ex)
//                {
//                    Logger.Log($"管道 IO 錯誤 (斷線或客戶端離開): {ex.Message}，等待重連...", LoggingTarget.Input);
//                }
//                catch (Exception ex)
//                {
//                    Logger.Log($"伺服器錯誤: {ex.Message}，等待重連...", LoggingTarget.Input);
//                }
//                finally
//                {
//                    pipeClient?.Dispose();
//                }

//                // 斷線後短暫延遲後重試，避免 CPU 過載
//                if (!token.IsCancellationRequested)
//                {
//                    await Task.Delay(1000, token).ConfigureAwait(false);
//                }
//            }

//            Logger.Log("Named Pipe Server 已停止。", LoggingTarget.Input);
//        }


//        private Task<object> getCurrentStateAsync()
//        {
//            var tcs = new TaskCompletionSource<object>();

//            game.Scheduler.Add(() =>
//            {
//                try
//                {
//                    var currentScreen = game.GetCurrentScreen();

//                    if (currentScreen is Player player && player.IsLoaded)
//                    {
//                        var gameplay = player.GameplayState;

//                        // 預設值，避免 null
//                        bool? hasFailed = null;
//                        bool? hasCompleted = null;
//                        double? healthAtJudgement = null;
//                        bool? isHit = null;
//                        double? healthIncrease = null;
//                        long? score = null;
//                        double? accuracy = null;
//                        int? combo = null;

//                        if (gameplay != null)
//                        {
//                            hasFailed = gameplay.HasFailed;
//                            hasCompleted = gameplay.HasCompleted;

//                            if (gameplay.LastJudgementResult.Value is not null) // 修正原碼 bug: 應檢查 not null
//                            {
//                                var jr = gameplay.LastJudgementResult.Value;
//                                healthAtJudgement = jr.HealthAtJudgement;
//                                isHit = jr.IsHit;
//                                healthIncrease = jr.HealthIncrease;
//                            }

//                            if (gameplay.ScoreProcessor != null)
//                            {
//                                score = gameplay.ScoreProcessor.TotalScore.Value;
//                                accuracy = gameplay.ScoreProcessor.Accuracy.Value;
//                                combo = gameplay.ScoreProcessor.Combo.Value;
//                            }
//                        }

//                        var state = new
//                        {
//                            IsInGame = true,
//                            HasFailed = hasFailed,
//                            HasCompleted = hasCompleted,
//                            HealthAtJudgement = healthAtJudgement,
//                            IsHit = isHit,
//                            HealthIncrease = healthIncrease,
//                            score,
//                            accuracy,
//                            combo
//                        };

//                        tcs.SetResult(state);
//                    }
//                    else
//                    {
//                        num_msg++;
//                        Logger.Log($"student: send data: {num_msg}", LoggingTarget.Input);

//                        var state = new
//                        {
//                            IsInGame = false,
//                            Time = game.Clock.CurrentTime,
//                            num_msg
//                        };
//                        tcs.SetResult(state);
//                    }
//                }
//                catch (Exception ex)
//                {
//                    tcs.SetException(ex);
//                }
//            });

//            return tcs.Task;
//        }

//        public void Dispose()
//        {
//            if (!cts.IsCancellationRequested)
//            {
//                Logger.Log("正在清理 Named Pipe Server...", LoggingTarget.Input);
//                cts.Cancel();
//            }
//            serverTask?.Wait(1000); // 等待任務結束
//            cts.Dispose();
//        }
//    }
//}
