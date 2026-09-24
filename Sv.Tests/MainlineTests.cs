using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;
using Mobile.Server;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Sv.Configuration;
using Sv.Database;
using Sv.Game;
using Sv.Gateway;
using Sv.Gateway.Handlers;
using Sv.Resources;
using Sv.Resources.Tables;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Sv.Tests;

public sealed class MainlineFixture
{
    public MainlineFixture()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../StaticResParser/output"));
        string? configured = Environment.GetEnvironmentVariable("SV_TEST_TABLE_ROOT");
        ResourcesLoader.Initialize(new GameDataOptions { TableRoot = configured ?? root });
        Assert.True(GameTableCatalog.Instance.IsLoaded, $"Missing original resource tables: {root}");
        GameDatabase.Initialize(new DatabaseOptions { Path = Path.Combine(Path.GetTempPath(), "Sv.Tests", Guid.NewGuid().ToString("N"), "players.db") });
    }

    public static Player NewPlayer() => GameDatabase.Instance.GetOrCreateByName("test-" + Guid.NewGuid().ToString("N"), 5004);
    public static T Table<T>(int id) where T : TableBase => GameTableCatalog.Instance.GetDataById<T>(id) ?? throw new InvalidDataException($"Missing {typeof(T).Name} {id}");
}

public sealed class MainlineTests(MainlineFixture fixture) : IClassFixture<MainlineFixture>
{
    private readonly MainlineFixture _fixture = fixture;

    [Fact]
    public void NewAccountStartsAtOriginalPrologue()
    {
        Assert.NotNull(_fixture);
        Player player = MainlineFixture.NewPlayer();
        lock (player.SyncRoot)
        {
            Assert.Equal(0, player.WeekNum.Week);
            Assert.Equal(0, player.WeekNum.Day);
            Assert.Equal("1", player.Story.Route);
            Assert.Empty(player.HeroMgr.HeroIds);
            Assert.Empty(player.EventTrigger.Completed);
            Assert.Empty(player.SaveData.NewbeeComp.Finished);
            Assert.Contains(player.EventTrigger.Available(), row => row.Id == 1);
            Assert.Equal(24, player.City.ActionVal);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void TutorialNeedsActualBattleAndResumesIntoWakeUp(int win)
    {
        Player player = MainlineFixture.NewPlayer();
        lock (player.SyncRoot)
        {
            Assert.True(player.EventTrigger.Start(1));
            Assert.True(player.EventTrigger.Trace("dialog", 1));
            Assert.False(player.EventTrigger.IsCompleted(1));
            player.Save();
        }
        player = Reload(player);
        lock (player.SyncRoot)
        {
            var entry = player.Combat.Enter(2, 104, [24]);
            Assert.NotNull(entry);
            Assert.Empty(player.HeroMgr.HeroIds);
            Assert.NotNull(player.Combat.Finish(2, 104, [24], [], win));
            Assert.True(player.EventTrigger.IsCompleted(1));
            Complete(player, 3);
            Complete(player, 6);
            Assert.True(player.Newbee.PassPrologue());
            Assert.Contains(player.EventTrigger.Available(), row => row.Id == 1000);
            Complete(player, 1000);
            Assert.True(player.EventTrigger.IsCompleted(1000));
        }
    }

    [Fact]
    public void NaturalTutorialUnlocksHeroesCityAndHighSchool()
    {
        Player player = MainlineFixture.NewPlayer();
        lock (player.SyncRoot)
        {
            Tutorial(player);
            Assert.Contains(24, player.HeroMgr.HeroIds);
            Assert.Equal(2, player.HeroMgr.HeroIds.Length);
            Assert.Equal(1, player.City.Blackcores()[1]);
            Assert.True(player.EventTrigger.IsCompleted(1047));
            Assert.Equal(0, player.City.FindArea(1)!.Status);
            Assert.Equal(1, player.City.FindArea(2)!.Status);
            Assert.True(player.EventTrigger.UpdateClientCondition(100, JsonSerializer.SerializeToElement("2:1")));
            Drain(player);
            int[] stages = player.City.FindArea(2)!.StageIds.ToArray();
            Assert.Equal(1101, stages[0]);
            while (player.City.FindArea(2)!.Status == 1)
            {
                Drain(player);
                int stage = player.City.FindArea(2)!.CurrentStage;
                Assert.NotNull(player.Combat.Enter(1, stage, player.HeroMgr.HeroIds, 2));
                Assert.NotNull(player.Combat.Finish(1, stage, player.HeroMgr.HeroIds, [], 1));
            }
            Assert.Equal(0, player.City.FindArea(2)!.Status);
            Drain(player);
            Assert.True(player.City.Patrol(2, player.HeroMgr.HeroIds, out string? error), error);
            Drain(player);
            Assert.True(player.City.Blackcores()[2] == 1, Diagnostic(player));
            player.City.FinishPatrol();
            player.Save();
            Player restored = Reload(player);
            Assert.Equal(player.City.ActionVal, restored.City.ActionVal);
            Assert.Equal(player.EventTrigger.Completed, restored.EventTrigger.Completed);
        }
    }

    [Fact]
    public void InvalidOptionsAndTeamsDoNotPartiallyMutate()
    {
        Player player = MainlineFixture.NewPlayer();
        lock (player.SyncRoot)
        {
            Assert.True(player.EventTrigger.Start(1));
            byte[] before = player.SaveToBlob();
            Assert.False(player.EventTrigger.SelectOption(1, 8029, 1));
            Assert.Equal(before, player.SaveToBlob());
            Tutorial(player);
            before = player.SaveToBlob();
            Assert.False(player.City.Build(1, 2, 2, [24, 24], out _));
            Assert.False(player.City.Build(1, 2, 2, [24, 999999], out _));
            Assert.Equal(before, player.SaveToBlob());
            player.City.ActionVal = 0;
            before = player.SaveToBlob();
            Assert.False(player.City.LevelUpArea(1, 1, player.HeroMgr.HeroIds, out _));
            Assert.Equal(before, player.SaveToBlob());
        }
    }

    [Fact]
    public void BattleRetryAndSettlementAreDurableAndOneShot()
    {
        Player player = MainlineFixture.NewPlayer();
        lock (player.SyncRoot)
        {
            Tutorial(player);
            int stage = player.City.FindArea(2)!.CurrentStage;
            int[] heroes = player.HeroMgr.HeroIds;
            Assert.NotNull(player.Combat.Enter(1, stage, heroes, 2));
            int action = player.City.ActionVal;
            player.Save();
            Player restored = Reload(player);
            lock (restored.SyncRoot)
            {
                Assert.False(restored.Combat.IsActive);
                Assert.NotNull(restored.Combat.Enter(1, stage, heroes, 2));
                Assert.Equal(action, restored.City.ActionVal);
                Assert.NotNull(restored.Combat.Finish(1, stage, heroes, [], 0));
                Assert.Equal(1, restored.City.FindArea(2)!.Status);
                Assert.Equal(stage, restored.City.FindArea(2)!.CurrentStage);
                Drain(restored);
                Assert.NotNull(restored.Combat.Enter(1, stage, heroes, 2));
                Assert.NotNull(restored.Combat.Finish(1, stage, heroes, [], 1));
                int money = restored.Profile.Money;
                restored.Save();
                Player final = Reload(restored);
                lock (final.SyncRoot)
                {
                    Assert.NotNull(final.Combat.Finish(1, stage, heroes, [], 1));
                    Assert.Equal(money, final.Profile.Money);
                }
            }
        }
    }

    [Fact]
    public void ZeroActionAndPendingDailySettlementSurviveLogin()
    {
        Player player = MainlineFixture.NewPlayer();
        lock (player.SyncRoot)
        {
            Tutorial(player);
            Assert.True(player.City.PrepareNextDay());
            Assert.Equal(0, player.City.ActionVal);
            player.Save();
            Player restored = Reload(player);
            lock (restored.SyncRoot)
            {
                Assert.Equal(0, restored.City.ActionVal);
                Drain(restored);
                Assert.True(restored.WeekNum.Advance(), Diagnostic(restored));
                Assert.Equal(1, restored.WeekNum.Day);
                restored.Save();
                Player final = Reload(restored);
                lock (final.SyncRoot)
                {
                    Assert.True(final.WeekNum.Advance());
                    Assert.Equal(1, final.WeekNum.Day);
                    Assert.Equal(24, final.City.ActionVal);
                    Assert.Single(final.SaveData.WeekNumComp.Settlements);
                }
            }
        }
    }

    [Fact]
    public void SevenDaysReachAResourceDefinedEnding()
    {
        Player player = MainlineFixture.NewPlayer();
        lock (player.SyncRoot)
        {
            Tutorial(player);
            for (int day = 0; day < 7; day++)
            {
                Drain(player);
                Assert.Equal(day, player.WeekNum.Day);
                Assert.True(player.EventTrigger.UpdateClientCondition(102, JsonSerializer.SerializeToElement(new[] { 4 })), Diagnostic(player));
                Drain(player);
                Assert.True(player.WeekNum.Advance(), Diagnostic(player));
                player.WeekNum.ClearSettlement();
            }
            Assert.True(player.EventTrigger.UpdateClientCondition(102, JsonSerializer.SerializeToElement(new[] { 1 })));
            Drain(player);
            Assert.Contains(player.WeekNum.EndingId, new[] { 1, 2, 6, 7 });
            Assert.True(player.WeekNum.ChapterPending);
            Assert.Equal(7, player.SaveData.WeekNumComp.Settlements.Count);
            Assert.Equal(0, player.City.ActionVal);
            player.Save();
        }
    }

    [Theory]
    [InlineData(0, 0, 7)]
    [InlineData(1, 0, 7)]
    [InlineData(1, 1, 2)]
    public void PurifiedBlackcoresAndFinalBattleResultsSelectTheEnding(int firstWin, int lastWin, int ending)
    {
        Player player = FinalPlayer();
        lock (player.SyncRoot)
        {
            player.SaveData.EventTriggerComp.CompletedEvents.Add([6010, 7038, 7040]);
            for (int area = 1; area <= 4; area++) player.City.SetBlackcore(area, 1);
            for (int area = 5; area <= 8; area++) player.City.SetBlackcore(area, 2);
            Assert.True(player.EventTrigger.UpdateClientCondition(102, JsonSerializer.SerializeToElement(new[] { 1 })));
            Assert.Contains(player.EventTrigger.Available(), row => row.Id == 8037);
            Complete(player, 8037);
            FinishEventBattle(player, 8061, firstWin);
            if (firstWin == 1) FinishEventBattle(player, 8003, lastWin);
            if (ending == 2) { Complete(player, 8043); Complete(player, 8044); }
            else Complete(player, 8028);
            Assert.Equal(ending, player.WeekNum.EndingId);
            Assert.True(player.WeekNum.ChapterPending);
            Assert.NotEmpty(player.SaveData.WeekNumComp.ScoreJson);
            int money = player.Profile.Money;
            int coins = player.Profile.SummonCoin;
            player.WeekNum.Conclude(ending);
            Assert.Equal(money, player.Profile.Money);
            Assert.Equal(coins, player.Profile.SummonCoin);
            player.Save();
        }
        Player restored = Reload(player);
        lock (restored.SyncRoot)
        {
            Assert.Equal(ending, restored.WeekNum.EndingId);
            List<string> notifications = [];
            restored.Notification += (method, _) => notifications.Add(method);
            StoryHandlers.OnRequest(restored, "pullEvents", []);
            StoryHandlers.OnRequest(restored, "pullEvents", []);
            Assert.Single(notifications, method => method == "showWeekResult");
            Assert.Single(notifications, method => method == "syncWeeknumScoreInfo");
            Assert.DoesNotContain("cgRewards", notifications);
            Assert.Equal(player.Profile.SummonCoin, restored.Profile.SummonCoin);
            Assert.False(restored.Story.Enter("1"));
            Assert.False(restored.WeekNum.Advance());
        }
    }

    [Theory]
    [InlineData(0, 7)]
    [InlineData(1, 6)]
    public void LivingCorpseBranchRespectsWinAndLoss(int win, int ending)
    {
        Player player = FinalPlayer();
        lock (player.SyncRoot)
        {
            player.EventTrigger.Queue(8023);
            Complete(player, 8023);
            FinishEventBattle(player, 8022, win);
            Complete(player, win == 1 ? 8024 : 8028);
            Assert.Equal(ending, player.WeekNum.EndingId);
        }
    }

    [Fact]
    public void FinalChoicePersistsAndCannotSelectUnsupportedEnding()
    {
        Player player = FinalPlayer();
        lock (player.SyncRoot)
        {
            player.EventTrigger.Queue(8004);
            Assert.True(player.EventTrigger.Start(8004));
            byte[] before = player.SaveToBlob();
            Assert.False(player.EventTrigger.SelectOption(8004, 8005, 1));
            Assert.Equal(before, player.SaveToBlob());
            Assert.True(player.EventTrigger.SelectOption(8004, 8029, 2));
            player.Save();
        }
        Player restored = Reload(player);
        lock (restored.SyncRoot)
        {
            Assert.Contains(restored.EventTrigger.Choices, choice => choice.EventId == 8004 && choice.Option == 2);
            Assert.Contains(8029, restored.EventTrigger.Processing);
            Complete(restored, 8029);
            Assert.Equal(1, restored.WeekNum.EndingId);
        }
    }

    [Fact]
    public void BannedHeroesAndCinematicLocksSurviveReloadAndPreventPartialCharges()
    {
        Player player = MainlineFixture.NewPlayer();
        lock (player.SyncRoot)
        {
            Tutorial(player);
            int[] heroes = player.HeroMgr.HeroIds;
            player.HeroMgr.SetBanned(heroes[1], 200);
            byte[] before = player.SaveToBlob();
            Assert.False(player.City.Patrol(1, heroes, out _));
            Assert.False(player.City.Build(1, 2, 2, heroes, out _));
            Assert.False(player.City.LevelUpArea(1, 1, heroes, out _));
            Assert.Null(player.Combat.Enter(1, player.City.FindArea(2)!.CurrentStage, heroes, 2));
            Assert.Equal(before, player.SaveToBlob());
            player.HeroMgr.SetBanned(heroes[1], 0);
            player.HeroMgr.LockCinematic([heroes[1]]);
            player.Save();
        }
        Player restored = Reload(player);
        lock (restored.SyncRoot)
        {
            Assert.Single(restored.HeroMgr.CineLocked);
            Assert.False(restored.HeroMgr.ValidateTeam(restored.HeroMgr.HeroIds));
            int[] heroes = restored.HeroMgr.HeroIds;
            Assert.True(restored.HeroMgr.ConsumeFatigue(heroes[1], restored.HeroMgr.Find(heroes[1])!.Fatigue));
            byte[] before = restored.SaveToBlob();
            Assert.False(restored.City.Patrol(1, heroes, out _));
            Assert.Equal(before, restored.SaveToBlob());
        }
    }

    [Fact]
    public void CombatPayloadUsesEnemyBlackcoresAndPresetHeroesWithoutGrantingOwnership()
    {
        Player player = MainlineFixture.NewPlayer();
        lock (player.SyncRoot)
        {
            player.City.SetBlackcore(1, 1);
            player.City.SetBlackcore(2, 2);
            Assert.True(player.EventTrigger.Start(1));
            var entry = player.Combat.Enter(2, 104, [24])!;
            var combat = (Dictionary<string, object>)entry["c"];
            Assert.Equal(new[] { 2 }, combat["blackcore"]);
            Assert.Empty(player.HeroMgr.HeroIds);
            Assert.Single((Dictionary<string, object>)combat["heroes"]);
            var skin = (Dictionary<string, object>)((object[])combat["skins"])[0];
            Assert.All(new[] { "i", "u", "cd", "pd", "dn" }, key => Assert.True(skin.ContainsKey(key)));
        }
    }

    [Fact]
    public void CgRewardsAndMainlineResetPreservePermanentAssetsAndHistory()
    {
        Player player = FinalPlayer();
        lock (player.SyncRoot)
        {
            player.WeekNum.Conclude(7);
            int cg = MainlineFixture.Table<EndingData>(7).Int("cg");
            int money = player.Profile.Money;
            int coins = player.Profile.SummonCoin;
            Assert.Contains(cg, player.Story.CurrentCgs);
            Assert.False(player.Story.AddCg(cg));
            Assert.Equal(money, player.Profile.Money);
            Assert.Equal(coins, player.Profile.SummonCoin);
            player.WeekNum.ResetMainline();
            Assert.Equal(0, player.WeekNum.Week);
            Assert.Equal(0, player.WeekNum.Day);
            Assert.Equal(24, player.City.ActionVal);
            Assert.Empty(player.Story.CurrentCgs);
            Assert.Empty(player.EventTrigger.Completed);
            Assert.Empty(player.HeroMgr.HeroIds);
            Assert.Contains(7, player.Story.Endings);
            Assert.Contains(cg, player.Story.Cgs);
            Assert.Equal(coins, player.Profile.SummonCoin);
            Assert.Contains(player.EventTrigger.Available(), row => row.Id == 1);
            player.Save();
        }
    }

    [Theory]
    [InlineData("areaBuildRequest", "area_build_reply")]
    [InlineData("combatOfflineRequest", "combatOfflineReply")]
    [InlineData("requestEnterStoryByEnding", "replyEnterStoryByEnding")]
    [InlineData("unknown_city_action", "showMessageStr")]
    public async Task FailedAndUnsupportedRequestsReleaseCallbacksAndDoNotCharge(string method, string reply)
    {
        Player player = MainlineFixture.NewPlayer();
        await WithSession(player, async (handler, session, stream) =>
        {
            byte[] before = player.SaveToBlob();
            handler.OnRequest(session, method, new() { ["_cbid_"] = 31 });
            var packets = await ReadUntilCallback(stream);
            Assert.Contains(packets, packet => packet.Name == reply);
            Assert.False(packets[^1].Args["_r_"]["_result_"].AsBoolean);
            Assert.Equal(31, packets[^1].Args["_cbid_"].AsInt32);
            Assert.Equal(before, player.SaveToBlob());
        });
    }

    [Fact]
    public async Task RejectedBattleSettlementReturnsEmptyRewardAndKeepsStageRetryable()
    {
        Player player = MainlineFixture.NewPlayer();
        lock (player.SyncRoot)
        {
            Tutorial(player);
            Assert.NotNull(player.Combat.Enter(1, player.City.FindArea(2)!.CurrentStage, player.HeroMgr.HeroIds, 2));
        }
        await WithSession(player, async (handler, session, stream) =>
        {
            int stage = player.City.FindArea(2)!.CurrentStage;
            int money = player.Profile.Money;
            int action = player.City.ActionVal;
            handler.OnRequest(session, "combatOfflineFinish", new() { ["t"] = 1, ["s"] = stage, ["h"] = new BsonArray { 999999 }, ["w"] = 1 }, callback: 40);
            var packets = await ReadUntilCallback(stream);
            var reward = Assert.Single(packets, packet => packet.Name == "combatOfflineReward");
            Assert.Empty(reward.Args["r"].AsBsonDocument);
            Assert.False(packets[^1].Args["_r_"]["_result_"].AsBoolean);
            Assert.Equal(stage, player.City.FindArea(2)!.CurrentStage);
            Assert.Equal(money, player.Profile.Money);
            Assert.Equal(action, player.City.ActionVal);
            Assert.DoesNotContain(player.Combat.Settled, battle => battle.Type == 1);
            lock (player.SyncRoot) Assert.NotNull(player.Combat.Enter(1, stage, player.HeroMgr.HeroIds, 2));
            Assert.Equal(action, player.City.ActionVal);
        });
    }

    [Fact]
    public async Task AvatarRefreshUsesNativeBinarySnapshotAndDoesNotRestoreAction()
    {
        Player player = MainlineFixture.NewPlayer();
        lock (player.SyncRoot) { player.City.ActionVal = 0; player.Save(); }
        await WithSession(player, async (handler, session, stream) =>
        {
            handler.OnRequest(session, "updateClientAvatarAsk", [], callback: 50);
            var packets = await ReadUntilCallback(stream);
            var snapshot = Assert.Single(packets, packet => packet.Name == "updateClientAvatarReply");
            Assert.True(snapshot.Args["s"].IsBsonBinaryData);
            using var input = new MemoryStream(snapshot.Args["s"].AsBsonBinaryData.Bytes);
            using var zipped = new System.IO.Compression.ZLibStream(input, System.IO.Compression.CompressionMode.Decompress);
            using var decoded = new MemoryStream();
            zipped.CopyTo(decoded);
            using var data = JsonDocument.Parse(MessagePack.MessagePackSerializer.ConvertToJson(decoded.ToArray()));
            Assert.Equal(0, data.RootElement.GetProperty("city").GetProperty("av").GetInt32());
            Assert.Equal(0, player.City.ActionVal);
        });
    }

    [Fact]
    public async Task ReliableRequestReplaysWithCurrentCallbackWithoutDuplicateCost()
    {
        Player player = MainlineFixture.NewPlayer();
        lock (player.SyncRoot) Tutorial(player);
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        using TcpClient client = new();
        Task connect = client.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
        using TcpClient accepted = await listener.AcceptTcpClientAsync();
        await connect;
        using GatewayHostedService server = new(NullLogger<GatewayHostedService>.Instance);
        GatewaySession session = new(1, accepted.GetStream(), server);
        session.BindPlayer(player);
        MainlineHandlers handler = new();
        BsonDocument args = new() { ["aid"] = 1, ["hs"] = new BsonArray(player.HeroMgr.HeroIds) };
        handler.OnRequest(session, "enterPatrolRequest", args, 1, 7);
        List<(string Name, BsonDocument Args)> first = await ReadUntilCallback(client.GetStream());
        int action = player.City.ActionVal;
        int money = player.Profile.Money;
        handler.OnRequest(session, "enterPatrolRequest", args, 1, 8);
        var second = await ReadUntilCallback(client.GetStream());
        Assert.Equal(action, player.City.ActionVal);
        Assert.Equal(money, player.Profile.Money);
        Assert.Equal(7, first[^1].Args["_cbid_"].AsInt32);
        Assert.Equal(8, second[^1].Args["_cbid_"].AsInt32);
        Assert.Contains(first, packet => packet.Name == "reliableRpcAck");
        Assert.Contains(first, packet => packet.Name == "enter_patrol_reply");
        Assert.Single(player.SaveData.RpcComp.Receipts);
        session.Close();
    }

    [Theory]
    [InlineData(16, "[0,0]", 0, true)]
    [InlineData(16, "(0,2]", 0, false)]
    [InlineData(4, "[0,6]", 7, false)]
    public void ResourceIntervalsFollowClientBoundaries(int kind, string expression, int actual, bool expected) =>
        Assert.Equal(expected, new ResourceCondition(kind, expression).Matches(actual));

    [Fact]
    public void ResourceParserNeverExecutesExpressions() => Assert.Throws<FormatException>(() => new ResourceCondition(1, "__import__('os').system('bad')"));

    private static async Task<List<(string Name, BsonDocument Args)>> ReadUntilCallback(NetworkStream stream)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        List<(string Name, BsonDocument Args)> packets = [];
        while (true)
        {
            byte[] header = new byte[6];
            await stream.ReadExactlyAsync(header, timeout.Token);
            byte[] data = new byte[BinaryPrimitives.ReadInt32LittleEndian(header) - 2];
            await stream.ReadExactlyAsync(data, timeout.Token);
            EntityMessage message = EntityMessage.Parser.ParseFrom(data);
            string name = GatewayRouter.GetRpcName(Convert.ToHexString(message.Method.Md5.Span));
            packets.Add((name, BsonSerializer.Deserialize<BsonDocument>(message.Parameters.ToByteArray())));
            if (name == "serverCallbackCarrier") return packets;
        }
    }

    private static async Task WithSession(Player player, Func<MainlineHandlers, GatewaySession, NetworkStream, Task> action)
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        using TcpClient client = new();
        Task connect = client.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
        using TcpClient accepted = await listener.AcceptTcpClientAsync();
        await connect;
        using GatewayHostedService server = new(NullLogger<GatewayHostedService>.Instance);
        GatewaySession session = new(1, accepted.GetStream(), server);
        lock (player.SyncRoot) session.BindPlayer(player);
        try { await action(new MainlineHandlers(), session, client.GetStream()); }
        finally { session.Close(); }
    }

    private static Player FinalPlayer()
    {
        Player player = MainlineFixture.NewPlayer();
        lock (player.SyncRoot)
        {
            player.WeekNum.Day = 7;
            player.City.ActionVal = 0;
            foreach (int hero in new[] { 24, 6, 44 }) player.HeroMgr.Unlock(hero);
            player.Save();
        }
        return player;
    }

    private static void FinishEventBattle(Player player, int id, int win)
    {
        EventContentData row = MainlineFixture.Table<EventContentData>(id);
        Assert.True(player.EventTrigger.Start(id), $"Cannot start final event {id}");
        Assert.True(player.EventTrigger.Trace("dialog", row.DialogueId));
        Assert.False(player.EventTrigger.IsCompleted(id));
        int[] heroes = player.HeroMgr.AvailableHeroIds.Take(3).ToArray();
        Assert.NotNull(player.Combat.Enter(2, row.Battle, heroes));
        Assert.NotNull(player.Combat.Finish(2, row.Battle, heroes, [], win));
        Assert.True(player.EventTrigger.IsCompleted(id));
    }

    private static Player Reload(Player player)
    {
        Player restored = GameDatabase.Instance.GetByUid(player.Uid)!;
        lock (restored.SyncRoot) { restored.OnLogin(); restored.Save(); }
        return restored;
    }

    private static string Diagnostic(Player player) => $"day={player.WeekNum.Day} advance={player.WeekNum.CanAdvance()} patrol={player.City.PatrolArea} " +
        $"processing={string.Join(',', player.EventTrigger.Processing)} available={string.Join(',', player.EventTrigger.Available().Select(row => row.Id))} " +
        $"completed={string.Join(',', player.EventTrigger.Completed)}";

    private static void Complete(Player player, int id)
    {
        EventContentData row = MainlineFixture.Table<EventContentData>(id);
        Assert.True(player.EventTrigger.Start(id), $"Cannot start {id} {row.Text("name")}: {string.Join(";", player.EventTrigger.BlockingConditions(id))}");
        if (row.DialogueId > 0)
        {
            EventDialogueData dialog = MainlineFixture.Table<EventDialogueData>(row.DialogueId);
            int next = dialog.Int("finish_event");
            int option = 1;
            if (dialog.Ints("opts").Length > 0)
            {
                option = dialog.Ints("opts").FirstOrDefault(value => !player.EventTrigger.IsCompleted(dialog.Int($"opt{value}_event")), dialog.Ints("opts")[0]);
                if (id == 1018) option = 4;
                next = dialog.Int($"opt{option}_event");
            }
            if (next > 0) Assert.True(player.EventTrigger.SelectOption(id, next, option), $"Rejected option {id}/{option}->{next}");
            else Assert.True(player.EventTrigger.Trace("dialog", row.DialogueId), $"Rejected dialog {id}");
        }
        int battle = row.Battle > 0 ? row.Battle : row.Ints("contentFinishBattle").FirstOrDefault();
        if (battle > 0 && player.EventTrigger.Find(id) is not null)
        {
            MissionData mission = MainlineFixture.Table<MissionData>(battle);
            int type = row.Battle > 0 ? 2 : 1;
            int[] heroes = mission.PresetHeroes.Length > 0 ? mission.PresetHeroes : player.HeroMgr.AvailableHeroIds.Take(3).ToArray();
            Assert.True(player.Combat.Enter(type, battle, heroes, mission.Area) is not null, $"Cannot enter event={id} battle={battle} type={type} heroes={string.Join(',', heroes)}");
            Assert.NotNull(player.Combat.Finish(type, battle, heroes, [], 1));
        }
        player.EventTrigger.Refresh(false);
    }

    private static void Drain(Player player, int stopAt = 0)
    {
        for (int i = 0; i < 150; i++)
        {
            player.EventTrigger.Refresh(false);
            if (stopAt > 0 && player.EventTrigger.IsCompleted(stopAt)) return;
            int next = player.EventTrigger.Processing.FirstOrDefault();
            if (next == 0) next = player.EventTrigger.Available().FirstOrDefault(row => !row.Patrol || player.City.PatrolArea > 0)?.Id ?? 0;
            if (next == 0) return;
            Complete(player, next);
        }
        Assert.Fail("Event chain did not quiesce");
    }

    private static void Tutorial(Player player)
    {
        Drain(player, 1021);
        Assert.True(player.EventTrigger.IsCompleted(1021));
        Assert.True(player.Newbee.OpenSummon(0));
        Assert.NotNull(player.Newbee.DrawSummon(0, 2));
        Drain(player, 1010);
        Assert.True(player.EventTrigger.IsCompleted(1010));
        Assert.True(player.City.Patrol(1, player.HeroMgr.HeroIds, out string? error), error);
        Drain(player, 1047);
        Assert.True(player.EventTrigger.IsCompleted(1047), string.Join(",", player.EventTrigger.Completed));
        player.City.FinishPatrol();
    }
}
