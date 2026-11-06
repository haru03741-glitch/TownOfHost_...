using System;
using System.Collections.Generic;
using HarmonyLib;

using TownOfHostY;

namespace TownOfHostL.Core;

/// <summary>
/// 会議以外の NotifyRoles をフレームの終わりまで溜め、同じ見る人 (seer) への組み立てを 1 回にまとめる。
/// キル 1 回で役職のフックから何度も呼ばれても、全員×全員の名前を組み立てるのはフレームに 1 回で済む。
/// 送る順番が意味を持つ所 (会議の開始・試合の終了) の前では、溜まっている分を先に吐き出す。
/// </summary>
public static class NotifyRolesQueue
{
    [Flags]
    private enum Mode : byte
    {
        Requested = 1,
        NoCache = 2,
        ForceLoop = 4,
    }

    private static Mode all;
    private static readonly Dictionary<byte, Mode> bySeer = new();
    private static readonly List<(byte seer, Mode mode)> flushBuffer = new();
    private static bool flushing;

    public static bool HasPending => all != 0 || bySeer.Count != 0;

    public static void Enqueue(PlayerControl seer, bool noCache, bool forceLoop)
    {
        var mode = Mode.Requested | (noCache ? Mode.NoCache : 0) | (forceLoop ? Mode.ForceLoop : 0);
        if (seer == null)
        {
            all |= mode;
            return;
        }
        bySeer.TryGetValue(seer.PlayerId, out var current);
        bySeer[seer.PlayerId] = current | mode;
    }

    /// <summary>溜まっている NotifyRoles を今すぐ走らせる。</summary>
    public static void Flush()
    {
        if (flushing || !HasPending) return;
        flushing = true;
        try
        {
            // 走らせている間に積まれた分は次の Flush に回すため、先に取り出して空にする
            var allMode = all;
            all = 0;
            flushBuffer.Clear();
            foreach (var (seer, mode) in bySeer)
            {
                // 全員分の呼び出しで同じかそれ以上の指定が済むなら、個別には呼ばない
                if (allMode != 0 && (mode & ~allMode) == 0) continue;
                flushBuffer.Add((seer, mode));
            }
            bySeer.Clear();

            // 1 つの呼び出しの例外で、残りの見る人の分まで捨てない
            if (allMode != 0)
                Run(null, allMode);
            foreach (var (seerId, mode) in flushBuffer)
            {
                var seer = Players.ById(seerId);
                if (seer == null) continue;
                Run(seer, mode);
            }
        }
        finally
        {
            flushBuffer.Clear();
            flushing = false;
        }
    }

    private static void Run(PlayerControl seer, Mode mode)
    {
        try
        {
            Utils.NotifyRolesNow(false, seer, mode.HasFlag(Mode.NoCache), mode.HasFlag(Mode.ForceLoop));
        }
        catch (Exception ex)
        {
            TownOfHostY.Logger.Exception(ex, "NotifyRolesQueue");
        }
    }

    public static void Clear()
    {
        all = 0;
        bySeer.Clear();
    }

    // 会議が始まると NotifyRoles は何もしなくなる。溜まっている分は会議の前に送る
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.StartMeeting))]
    private static class StartMeetingPatch
    {
        [HarmonyPriority(Priority.First)]
        public static void Prefix() => Flush();
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameEnd))]
    private static class GameEndPatch
    {
        [HarmonyPriority(Priority.First)]
        public static void Prefix() => Flush();
    }
}
