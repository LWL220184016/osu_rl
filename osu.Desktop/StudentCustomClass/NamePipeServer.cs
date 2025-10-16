using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Logging;
using osu.Framework.Screens;
using osu.Game.Screens.Play;

namespace osu.Desktop.StudentCustomClass
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
            this.apiInputHandler = handler;

            // 註冊退出事件，當主程式關閉時自動清理
            AppDomain.CurrentDomain.ProcessExit += (_, __) => Dispose();
        }

        public void Start()
        {
            serverTask = RunServerAsync(cts.Token);
        }

        private async Task RunServerAsync(CancellationToken token)
        {
            Console.WriteLine("C# Named Pipe Server 啟動中...");

            using (var pipeServer = new NamedPipeServerStream(
                "HighPerfPipe", PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
            {
                Console.WriteLine("等待 Python 客戶端連接...");
                await pipeServer.WaitForConnectionAsync(token).ConfigureAwait(false);
                Console.WriteLine("客戶端已連接！");

                using var reader = new StreamReader(pipeServer, Encoding.UTF8);
                using var writer = new StreamWriter(pipeServer, Encoding.UTF8) { AutoFlush = true };

                // 啟動讀取循環
                _ = Task.Run(async () =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        string? line = await reader.ReadLineAsync();
                        if (line == null) break;
                        Logger.Log($"student: 收到: {line}", LoggingTarget.Runtime, LogLevel.Important);
                    }
                }, token);

                // 持續發送遊戲狀態
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var state = await getCurrentStateAsync();
                        string json = JsonSerializer.Serialize(state);
                        await writer.WriteLineAsync(json);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"發送狀態時出錯: {ex.Message}");
                    }

                    await Task.Delay(50, token); // 每 50ms 發送一次 (20Hz)，可自行調整
                }
            }
        }

        private Task<object> getCurrentStateAsync()
        {
            var tcs = new TaskCompletionSource<object>();

            game.Scheduler.Add(() =>
            {
                try
                {
                    IScreen currentScreen = game.GetCurrentScreen();

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

                            if (gameplay.LastJudgementResult.Value is null)
                            {
                                var jr = gameplay.LastJudgementResult.Value;
                                healthAtJudgement = jr.HealthAtJudgement;
                                isHit = jr.IsHit;
                                healthIncrease = jr.HealthIncrease;
                            }

                            if (gameplay.ScoreProcessor != null)
                            {
                                score = gameplay.ScoreProcessor.TotalScore.Value;
                                accuracy = gameplay.ScoreProcessor.Accuracy.Value;
                                combo = gameplay.ScoreProcessor.Combo.Value;
                            }
                        }

                        var state = new
                        {
                            IsInGame = true,
                            HasFailed = hasFailed,
                            HasCompleted = hasCompleted,
                            HealthAtJudgement = healthAtJudgement,
                            IsHit = isHit,
                            HealthIncrease = healthIncrease,
                            score = score,
                            accuracy = accuracy,
                            combo = combo
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

        public void Dispose()
        {
            if (!cts.IsCancellationRequested)
            {
                Console.WriteLine("正在清理 Named Pipe Server...");
                cts.Cancel();
            }
            serverTask?.Wait(1000); // 等待任務結束
            cts.Dispose();
        }
    }
}
