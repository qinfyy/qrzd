namespace Sv.Database;

public sealed class PlayerEntity
{
    public long Uid { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ServerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] Data { get; set; } = [];
}
