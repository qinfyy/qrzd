using System.Text;
using System.Text.Json;
using MongoDB.Bson;

namespace Sv.Gateway;

public sealed record LocalAccount(string Name, int ServerId, long UserId, string AvatarId);

public sealed class LocalAccounts
{
    private readonly object sync = new();
    private readonly string path;
    private readonly Dictionary<string, LocalAccount> accounts = new(StringComparer.Ordinal);
    private readonly HashSet<long> activeUsers = [];
    private long nextUserId = 10001;

    public LocalAccounts(IHostEnvironment environment)
    {
        string directory = Path.Combine(environment.ContentRootPath, "data");
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "accounts.json");
        if (!File.Exists(path)) return;
        List<LocalAccount> saved = JsonSerializer.Deserialize<List<LocalAccount>>(File.ReadAllBytes(path))
            ?? throw new InvalidDataException("本地账号文件不是数组");
        HashSet<long> ids = [];
        HashSet<string> avatarIds = [];
        foreach (LocalAccount account in saved)
        {
            if (account.Name != NormalizeName(account.Name) || account.UserId < 1 || account.ServerId < 1 ||
                !ObjectId.TryParse(account.AvatarId, out _) || !ids.Add(account.UserId) || !avatarIds.Add(account.AvatarId))
                throw new InvalidDataException("本地账号文件包含无效或重复身份");
            accounts.Add(Key(account.Name, account.ServerId), account);
            nextUserId = Math.Max(nextUserId, checked(account.UserId + 1));
        }
    }

    public static string NormalizeName(string name)
    {
        string normalized = name.Trim().Normalize(NormalizationForm.FormC);
        if (normalized.Length is < 1 or > 64 || normalized.Any(char.IsControl))
            throw new InvalidDataException("调试账号名称不合法");
        return normalized;
    }

    public LocalAccount? Acquire(string name, int serverId)
    {
        name = NormalizeName(name);
        lock (sync)
        {
            string key = Key(name, serverId);
            if (!accounts.TryGetValue(key, out LocalAccount? account))
            {
                account = new LocalAccount(name, serverId, nextUserId, ObjectId.GenerateNewId().ToString());
                LocalAccount[] updated = [.. accounts.Values, account];
                string temporary = path + ".tmp";
                using (FileStream file = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(file, updated, new JsonSerializerOptions { WriteIndented = true });
                    file.Flush(true);
                }
                File.Move(temporary, path, true);
                accounts.Add(key, account);
                nextUserId = checked(nextUserId + 1);
            }
            return activeUsers.Add(account.UserId) ? account : null;
        }
    }

    public void Release(LocalAccount account)
    {
        lock (sync) activeUsers.Remove(account.UserId);
    }

    private static string Key(string name, int serverId) => $"{serverId}:{name}";
}
