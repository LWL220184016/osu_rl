//using System;
//using System.Buffers.Binary;
//using System.IO;
//using System.Net;
//using System.Net.Sockets;
//using System.Text;
//using System.Text.Json;
//using System.Threading;
//using System.Threading.Tasks;
//using osu.Framework.Logging;
//using osu.Framework.Screens;
//using osu.Game.Screens.Play;

//namespace osu.Desktop.StudentCustomClass.connections
//{
//    會卡死在不知道是 C# 讀取數據還是 Python 發送數據
//    internal class SocketServer : IDisposable
//    {
//        private readonly OsuGameDesktop game;
//        // private readonly ApiInputHandler apiInputHandler; // 根據需要保留
//        private readonly CancellationTokenSource cts = new CancellationTokenSource();
//        private Task? serverTask;
//        private int num_msg = 0;
//        private readonly TcpListener tcpListener;

//        public SocketServer(OsuGameDesktop game, ApiInputHandler handler)
//        {
//            this.game = game;
//            // this.apiInputHandler = handler; // 根據需要保留

//            // 監聽本地回環地址 127.0.0.1，避免防火牆彈窗
//            tcpListener = new TcpListener(IPAddress.Loopback, 8888);

//            // 註冊退出事件
//            AppDomain.CurrentDomain.ProcessExit += (_, __) => Dispose();
//        }

//        public void Start()
//        {
//            serverTask = RunServerAsync(cts.Token);
//        }

//        private async Task RunServerAsync(CancellationToken token)
//        {
//            Logger.Log("C# TCP Socket Server 啟動中...", LoggingTarget.Input);
//            tcpListener.Start();

//            while (!token.IsCancellationRequested)
//            {
//                try
//                {
//                    Logger.Log($"在 {tcpListener.LocalEndpoint} 等待 Python 客戶端連接...", LoggingTarget.Input);
//                    using TcpClient client = await tcpListener.AcceptTcpClientAsync(token).ConfigureAwait(false);
//                    Logger.Log($"客戶端已連接: {client.Client.RemoteEndPoint}", LoggingTarget.Input);

//                    await using NetworkStream stream = client.GetStream();

//                    // *** 這是主要的修改部分 ***
//                    // 我們不再使用 StreamReader/Writer，而是直接操作 stream

//                    // 用於接收訊息長度的緩衝區 (4位元組)
//                    var lengthBuffer = new byte[4];

//                    while (!token.IsCancellationRequested && client.Connected)
//                    {
//                        // 1. 讀取 4 位元組的訊息長度
//                        // ReadExactlyAsync (在 .NET 6+ 中) 能確保讀滿緩衝區
//                        // 為了相容性，我們使用手動迴圈讀取
//                        int bytesRead = 0;
//                        while (bytesRead < lengthBuffer.Length)
//                        {
//                            int read = await stream.ReadAsync(lengthBuffer, bytesRead, lengthBuffer.Length - bytesRead, token).ConfigureAwait(false);
//                            if (read == 0) throw new EndOfStreamException("客戶端在讀取長度時斷線。");
//                            bytesRead += read;
//                        }

//                        // 將網路位元組序 (大端) 轉為 int
//                        int messageLength = BinaryPrimitives.ReadInt32BigEndian(lengthBuffer);

//                        // 2. 根據讀取到的長度，接收實際的訊息內容
//                        var messageBuffer = new byte[messageLength];
//                        bytesRead = 0;
//                        while (bytesRead < messageLength)
//                        {
//                            int read = await stream.ReadAsync(messageBuffer, bytesRead, messageLength - bytesRead, token).ConfigureAwait(false);
//                            if (read == 0) throw new EndOfStreamException("客戶端在讀取內容時斷線。");
//                            bytesRead += read;
//                        }

//                        string request = Encoding.UTF8.GetString(messageBuffer);
//                        Logger.Log($"收到來自 Python 的請求: {request}", LoggingTarget.Input);

//                        // 3. 處理請求並準備回應
//                        var state = await GetCurrentStateAsync().ConfigureAwait(false);
//                        string jsonResponse = JsonSerializer.Serialize(state);
//                        byte[] responseBytes = Encoding.UTF8.GetBytes(jsonResponse);

//                        // 4. 準備長度前置並發送回應
//                        // 將回應的長度 (int) 轉為 4 個位元組 (大端序)
//                        BinaryPrimitives.WriteInt32BigEndian(lengthBuffer, responseBytes.Length);

//                        // 先發送長度，再發送內容
//                        await stream.WriteAsync(lengthBuffer, token).ConfigureAwait(false);
//                        await stream.WriteAsync(responseBytes, token).ConfigureAwait(false);
//                    }
//                }
//                catch (OperationCanceledException) { break; }
//                catch (IOException ex) { Logger.Log($"IO 錯誤 (客戶端可能已斷線): {ex.Message}", LoggingTarget.Input); }
//                catch (Exception ex) { Logger.Log($"伺服器錯誤: {ex.Message}", LoggingTarget.Input); }

//                Logger.Log("客戶端已斷線，等待下一個連接。", LoggingTarget.Input);
//            }


//            tcpListener.Stop();
//            Logger.Log("TCP Socket Server 已停止。", LoggingTarget.Input);
//        }

//        // getCurrentStateAsync 方法與您提供的版本完全相同
//        private Task<object> GetCurrentStateAsync()
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

//                            if (gameplay.LastJudgementResult.Value is not null)
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
//                            Score = score,
//                            Accuracy = accuracy,
//                            Combo = combo
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
//                            NumMsg = num_msg
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
//                Logger.Log("正在清理 TCP Socket Server...", LoggingTarget.Input);
//                cts.Cancel();
//            }
//            // 停止監聽器可以讓 AcceptTcpClientAsync 立即返回
//            tcpListener.Stop();
//            serverTask?.Wait(1000); // 等待任務結束
//            cts.Dispose();
//        }
//    }
//}
