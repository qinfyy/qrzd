using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Serilog;
using Sv.Configuration;

namespace Sv.Gateway;

public sealed class GatewayService(GatewayKeys keys, LocalAccounts accounts) : BackgroundService
{
    private readonly TcpListener listener = new(IPAddress.Any, Config.Server.GatewayPort);
    private readonly ConcurrentDictionary<long, Task> clients = new();
    private readonly SemaphoreSlim capacity = new(Config.Server.GatewayMaxConnections);
    private long nextConnectionId;
    private volatile bool listening;
    public bool Listening => listening;
    public int Connections => clients.Count;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        listener.Start(64);
        listening = true;
        Log.Information("QRZD Gateway TCP 监听 {Port}，公钥 SHA256={Fingerprint}", Config.Server.GatewayPort, keys.Fingerprint);
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(stoppingToken);
                IPAddress? peer = (client.Client.RemoteEndPoint as IPEndPoint)?.Address;
                if (peer is null || !ServerOptions.IsLocalAddress(peer) || !capacity.Wait(0))
                {
                    client.Dispose();
                    continue;
                }
                long id = Interlocked.Increment(ref nextConnectionId);
                Task task = RunClientAsync(id, client, stoppingToken);
                clients[id] = task;
                _ = task.ContinueWith(completed => clients.TryRemove(id, out _),
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            listening = false;
            listener.Stop();
            await Task.WhenAll(clients.Values);
        }
    }

    private async Task RunClientAsync(long id, TcpClient client, CancellationToken token)
    {
        using (client)
        {
            GatewaySession? session = null;
            try
            {
                client.NoDelay = true;
                GatewayConnection connection = new(client.GetStream(), Config.Server.GatewayMaxFrameBytes);
                session = new GatewaySession(id, connection, keys, accounts, Config.Server);
                await session.RunAsync(token);
            }
            catch (OperationCanceledException)
            {
                Log.Information("Gateway {ConnectionId} 会话结束 stage={Stage} reason={Reason}", id, session?.Stage, token.IsCancellationRequested ? "shutdown" : "timeout");
            }
            catch (EndOfStreamException) { Log.Information("Gateway {ConnectionId} 客户端断开 stage={Stage}", id, session?.Stage); }
            catch (Exception exception)
            {
                // 不输出密文、BSON、私钥或账号参数；坏包只结束当前连接。
                Log.Warning("Gateway {ConnectionId} 拒绝连接 stage={Stage} error={ErrorType}", id, session?.Stage, exception.GetType().Name);
            }
            finally { capacity.Release(); }
        }
    }

    public override void Dispose()
    {
        listener.Stop();
        capacity.Dispose();
        base.Dispose();
    }
}
