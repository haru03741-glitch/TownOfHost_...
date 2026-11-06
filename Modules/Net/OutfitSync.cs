using System.Collections.Generic;
using Hazel;
using InnerNet;
using TownOfHostL.Attributes;

namespace TownOfHostL.Modules.Net;

// 他のプレイヤーの外見 (色・帽子・スキン・バイザー・ペット・ネームプレート) をホストから変える。
// 帽子などの RPC は本来その本人だけが送るもので、ホストが他人の分を送る形にはしない。
// 代わりに、ホストが持ち主の PlayerInfo を serialize して送り、続けて本人を対象にした Shapeshift (変身の解除と同じ形) を
// 送って、受け手に既定の外見を着せ直させる。Shapeshift はふだんからホストが送るもの。
// 名前には触らない。名前は SetName の経路で配る。
// Data と Shapeshift は 1 人ずつ 1 つの GameData に収める (SendGuard が 1000 バイトを超えた束を分けると順序が保証されないため)。
// 謝辞: PlayerInfo の serialize と本人への Shapeshift で外見を配る方式は Endless Host Roles (Gurge44, GPL-3.0) が
// 始めたもので、End K not が取り入れた物に倣った。
public static class OutfitSync
{
    // PlayerInfo を送れない間 (試合開始の直後・AntiBlackout の退避中) に変えた人。送れるようになった所で送り直す
    static readonly HashSet<byte> pending = new();

    [GameModuleInitializer]
    public static void Init() => pending.Clear();

    public static bool Apply(PlayerControl target, NetworkedPlayerInfo.PlayerOutfit outfit)
    {
        if (!AmongUsClient.Instance.AmHost || target == null || outfit == null) return false;
        var data = target.Data;
        var current = data?.DefaultOutfit;
        if (current == null) return false;
        // ネームプレートは渡された物が空でない時だけ写すので、比べるのもその時だけ
        bool samePlate = string.IsNullOrEmpty(outfit.NamePlateId) || current.NamePlateId == outfit.NamePlateId;
        if (outfit.Compare(current) && samePlate) return false;

        // 渡された outfit を差し替えずに中身を写す。CamouflageOutfit のような共有の 1 つを全員の Data に入れないため
        current.ColorId = outfit.ColorId;
        current.HatId = outfit.HatId ?? "";
        current.SkinId = outfit.SkinId ?? "";
        current.VisorId = outfit.VisorId ?? "";
        current.PetId = outfit.PetId ?? "";
        if (!string.IsNullOrEmpty(outfit.NamePlateId)) current.NamePlateId = outfit.NamePlateId;

        // 変身中の人は、受け手でも変身したままにする。解除した時に本編が既定の外見 (= ここで変えた物) へ戻す
        bool shifted = IsShifted(target);
        if (!shifted) target.RawSetOutfit(current, PlayerOutfitType.Default);

        Send(target, data, refresh: !shifted);
        return true;
    }

    static void Send(PlayerControl target, NetworkedPlayerInfo data, bool refresh)
    {
        // 試合開始の直後は PlayerInfo の送信を止めている (送っても中身が空になる)。AntiBlackout の退避中は IsDead が偽りの値になっている
        if (GameDataSerializePatch.DontTouch || AntiBlackout.IsCached)
        {
            Logger.Info($"{data.PlayerName}: 今は PlayerInfo を送れないので外見の送信を後に回す", "OutfitSync");
            pending.Add(target.PlayerId);
            return;
        }
        pending.Remove(target.PlayerId);

        WriteAndSend(target, data, refresh);
    }

    // イントロの終わりと追放の画面の終わりに呼ぶ
    public static void FlushPending()
    {
        if (pending.Count == 0 || !AmongUsClient.Instance.AmHost) return;
        foreach (var id in new List<byte>(pending))
        {
            var pc = Utils.GetPlayerById(id);
            if (pc == null || pc.Data == null)
            {
                pending.Remove(id);
                continue;
            }
            Send(pc, pc.Data, refresh: !IsShifted(pc));
        }
    }

    static bool IsShifted(PlayerControl pc)
        => pc.CurrentOutfitType == PlayerOutfitType.Shapeshifted
            || (Main.CheckShapeshift.TryGetValue(pc.PlayerId, out var s) && s);

    static void WriteAndSend(PlayerControl target, NetworkedPlayerInfo data, bool refresh)
    {
        var writer = MessageWriter.Get(SendOption.Reliable);
        GameDataSerializePatch.SerializeMessageCount++;
        try
        {
            writer.StartMessage(5);
            writer.Write(AmongUsClient.Instance.GameId);

            writer.StartMessage(1);
            writer.WritePacked(data.NetId);
            data.Serialize(writer, false);
            writer.EndMessage();

            if (refresh)
            {
                writer.StartMessage(2);
                writer.WritePacked(target.NetId);
                writer.Write((byte)RpcCalls.Shapeshift);
                writer.WriteNetObject(target);
                writer.Write(false);
                writer.EndMessage();
            }

            writer.EndMessage();
            AmongUsClient.Instance.SendOrDisconnect(writer);
        }
        finally
        {
            GameDataSerializePatch.SerializeMessageCount--;
            writer.Recycle();
        }
    }
}
