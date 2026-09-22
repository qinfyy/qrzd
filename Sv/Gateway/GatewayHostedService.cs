using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Sv.Configuration;
using Sv.Game;
using Sv.Gateway.Protocol;
using Microsoft.Extensions.Logging;

namespace Sv.Gateway;

/// <summary>
/// 服务器！
/// </summary>
public sealed class GatewayHostedService(ILogger<GatewayHostedService> logger) : BackgroundService
{
    private readonly TcpListener _listener = new(IPAddress.Any, Config.Server.GatewayPort);
    private readonly ConcurrentDictionary<long, GatewaySession> _sessions = [];
    private readonly ConcurrentDictionary<long, GatewaySession> _sessionsByUid = [];
    private readonly ConcurrentDictionary<long, Task> _connectionTasks = [];
    private readonly SemaphoreSlim _capacity = new(Config.Server.GatewayMaxConnections);
    private long _nextConnectionId;
    private volatile bool _listening;

    public bool Listening => _listening;
    public int Connections => _sessions.Count;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        AeadTool.InitializeKeys(Config.Server.GatewayPrivateKeyPath);
        _listener.Start(64);
        _listening = true;
        logger.LogInformation("Game Gateway 监听 {Port}，公钥指纹 {Fingerprint}", Config.Server.GatewayPort, AeadTool.Fingerprint);
        return base.StartAsync(cancellationToken);
    }

    public void BindPlayer(GatewaySession session, Player player)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(player);

        if (_sessionsByUid.TryGetValue(player.Uid, out GatewaySession? existing) && !ReferenceEquals(existing, session))
        {
            logger.LogInformation("玩家 UID {PlayerUid} 已在连接 {OldConnId} 登录，踢下线并由连接 {NewConnId} 替换", player.Uid, existing.ConnectionId, session.ConnectionId);
            existing.Kick();
        }

        _sessionsByUid[player.Uid] = session;
        session.BindPlayer(player);
    }

    public bool RemoveSession(long uid) => _sessionsByUid.TryRemove(uid, out _);

    public GatewaySession? GetSessionByUid(long uid) =>
        _sessionsByUid.TryGetValue(uid, out GatewaySession? session) ? session : null;

    public Player? GetOnlinePlayerByUid(long uid) =>
        _sessionsByUid.TryGetValue(uid, out GatewaySession? session) ? session.Player : null;

    public bool IsOnline(long uid) => _sessionsByUid.ContainsKey(uid);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(stoppingToken);
                IPAddress? peer = (client.Client.RemoteEndPoint as IPEndPoint)?.Address;
                if (peer is null || !ServerOptions.IsLocalAddress(peer) || !_capacity.Wait(0))
                {
                    client.Dispose();
                    continue;
                }

                long connectionId = Interlocked.Increment(ref _nextConnectionId);
                Task task = RunClientAsync(connectionId, client, stoppingToken);
                _connectionTasks[connectionId] = task;
                _ = task.ContinueWith(completed =>
                {
                    _connectionTasks.TryRemove(connectionId, out _);
                    _sessions.TryRemove(connectionId, out _);
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            _listening = false;
            _listener.Stop();
            await Task.WhenAll(_connectionTasks.Values);
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
                session = new GatewaySession(id, client.GetStream(), this);
                _sessions[id] = session;
                await session.RunAsync();
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Gateway {ConnectionId} 会话结束 stage={Stage} reason={Reason}", id, session?.Stage, token.IsCancellationRequested ? "shutdown" : "timeout");
            }
            catch (EndOfStreamException)
            {
                logger.LogInformation("Gateway {ConnectionId} 客户端断开 stage={Stage}", id, session?.Stage);
            }
            catch (Exception exception)
            {
                logger.LogWarning("Gateway {ConnectionId} 异常中断 stage={Stage} error={ErrorType}: {Message}", id, session?.Stage, exception.GetType().Name, exception.Message);
            }
            finally
            {
                session?.Close();
                _capacity.Release();
            }
        }
    }

    public override void Dispose()
    {
        _listener.Stop();
        _capacity.Dispose();
        base.Dispose();
    }
}
