using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Pancake.Shared;

namespace Pancake.Services;

/// <summary>
/// 订阅班级实时通知：项目、布局、公告变化以及发给本机的指令。断线后按 1、2、4…最多 60 秒退避重连。
/// 事件在后台线程触发，订阅方负责切回界面线程。
/// </summary>
public sealed class CloudRealtimeClient : IDisposable
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public event Action<SyncEvent>? EventReceived;
    public event Action<bool>? ConnectionChanged;
    public bool IsConnected { get; private set; }

    public void Start(string serverUrl, Guid classId, string deviceToken)
    {
        Stop();
        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;
        Uri uri = new(serverUrl.TrimEnd('/').Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase).Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase) + $"/ws/classes/{classId}");
        _loop = Task.Run(() => RunAsync(uri, deviceToken, token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _loop = null;
        SetConnected(false);
    }

    private async Task RunAsync(Uri uri, string deviceToken, CancellationToken token)
    {
        TimeSpan delay = TimeSpan.FromSeconds(1);
        byte[] buffer = new byte[16 * 1024];
        while (!token.IsCancellationRequested)
        {
            using ClientWebSocket socket = new();
            socket.Options.SetRequestHeader("Authorization", "Bearer " + deviceToken);
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
            try
            {
                using (CancellationTokenSource connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    connectTimeout.CancelAfter(TimeSpan.FromSeconds(15));
                    await socket.ConnectAsync(uri, connectTimeout.Token);
                }
                SetConnected(true);
                delay = TimeSpan.FromSeconds(1);
                using MemoryStream message = new();
                while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, token);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    message.Write(buffer, 0, result.Count);
                    if (!result.EndOfMessage) continue;
                    string text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                    message.SetLength(0);
                    SyncEvent? value = null;
                    try { value = JsonSerializer.Deserialize<SyncEvent>(text, Web); } catch (JsonException) { }
                    if (value is not null) EventReceived?.Invoke(value);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is WebSocketException or HttpRequestException or OperationCanceledException or IOException) { }
            SetConnected(false);
            if (token.IsCancellationRequested) break;
            try { await Task.Delay(delay, token); } catch (OperationCanceledException) { break; }
            delay = TimeSpan.FromSeconds(Math.Min(60, delay.TotalSeconds * 2));
        }
    }

    private void SetConnected(bool value)
    {
        if (IsConnected == value) return;
        IsConnected = value;
        ConnectionChanged?.Invoke(value);
    }

    public void Dispose() => Stop();
}
