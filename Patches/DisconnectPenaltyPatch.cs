using AmongUs.Data;
using HarmonyLib;

namespace TownOfHostL;

// Based on https://github.com/Rabek009/MoreGamemodes (Patches/ClientPatch.cs CheckOnlinePermissionsPatch, GPL-3.0)
// 試合の途中で抜けると、本編は手元の保存データの banPoints を積み、一定の点数で部屋の作成と参加を一時的に塞ぐ。
// mod のホストは試合を中断して部屋を立て直すことが多いので、オンラインの権限を確かめる直前に点数を 0 に戻す。
// banPoints は手元だけで計算される値で、サーバー側の BAN とは関係しない。
[HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.CheckOnlinePermissions))]
static class DisconnectPenaltyPatch
{
    public static void Prefix()
    {
        DataManager.Player.Ban.banPoints = 0f;
    }
}
