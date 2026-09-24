using Google.Protobuf;
using Sv.Database;

namespace Sv.Game;

public sealed class RpcLogic(Player player) : PlayerLogicBase(player)
{
    private RpcComp Comp => Player.SaveData.RpcComp;
    public int MaxSequence => Comp.MaxSequence;
    public RpcReceipt? Find(int sequence) => Comp.Receipts.FirstOrDefault(receipt => receipt.Sequence == sequence)?.Clone();

    public void BeginConnection(int firstSequence)
    {
        // The original client resets its sequence to one on a fresh login, but retains it on reconnect.
        if (firstSequence != 1) return;
        Reset();
    }

    public void Remember(int sequence, string method, byte[] request, IEnumerable<(string Method, byte[] Parameters)> replies)
    {
        if (sequence <= Comp.MaxSequence) throw new InvalidOperationException("可靠请求序号已使用");
        RpcReceipt receipt = new() { Sequence = sequence, Method = method, Request = ByteString.CopyFrom(request) };
        receipt.Replies.Add(replies.Select(reply => new RpcReply { Method = reply.Method, Parameters = ByteString.CopyFrom(reply.Parameters) }));
        Comp.Receipts.Add(receipt);
        Comp.MaxSequence = sequence;
        while (Comp.Receipts.Count > 128) Comp.Receipts.RemoveAt(0);
        MarkDirty();
    }

    public void Reset()
    {
        Comp.MaxSequence = 0;
        Comp.Receipts.Clear();
        MarkDirty();
    }
}
