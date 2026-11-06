//参考：TOH-K
using HarmonyLib;

namespace TownOfHostL;



[HarmonyPatch(typeof(NetworkedPlayerInfo), nameof(NetworkedPlayerInfo.Serialize))]
class GameDataSerializePatch
{
   
    public static int SerializeMessageCount;
    
    public static bool DontTouch;

    public static bool Prefix(NetworkedPlayerInfo __instance, ref bool __result)
    {
        if (AmongUsClient.Instance == null || !GameStates.IsInGame)
        {
            __result = true;
            return true;
        }
        if (DontTouch)
        {
            __instance.ClearDirtyBits();
            __result = false;
            return false;
        }
        if (SerializeMessageCount > 0)
        {
            SendRealName(__instance);
            __result = true;
            return true;
        }

        if (Options.CurrentGameMode != CustomGameMode.Standard
            || !GameStates.IsMeeting
            || ExileController.Instance != null
            || AntiBlackout.IsCached)
        {
            SendRealName(__instance);
            return true;
        }
        __instance.ClearDirtyBits();
        __result = false;
        return false;
    }
    public static void Postfix(NetworkedPlayerInfo __instance)
    {
        if (swappedInfo == null) return;
        if (swappedInfo == __instance) __instance.DefaultOutfit.PlayerName = swappedName;
        swappedInfo = null;
        swappedName = null;
    }

    // 試合中、ホストの手元の名前はホストから見た表示 (役職・タスク数など) に書き換わっている。
    // PlayerInfo は全員に送られるので、送る間だけ本当の名前に戻し、ホストに見えている情報を他の人へ流さない。
    private static NetworkedPlayerInfo swappedInfo;
    private static string swappedName;
    private static void SendRealName(NetworkedPlayerInfo info)
    {
        var outfit = info.DefaultOutfit;
        if (outfit == null || !Main.AllPlayerNames.TryGetValue(info.PlayerId, out var realName) || outfit.PlayerName == realName) return;
        swappedInfo = info;
        swappedName = outfit.PlayerName;
        outfit.PlayerName = realName;
    }
}

[HarmonyPatch(typeof(NetworkedPlayerInfo), nameof(NetworkedPlayerInfo.MarkDirty))]
class NetworkedPlayerInfoMarkDirtyPatch
{
    public static bool Prefix() => GameDataSerializePatch.DontTouch is false;
}
