using System;
using System.Linq;

using TownOfHostL.Roles.AddOns.Common;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using TownOfHostL.Roles.Crewmate;
using TownOfHostL.Roles.Neutral;

namespace TownOfHostL.Core.Win;

/// <summary>
/// 試合の終わりが決まった後に、誰が勝つかを決める。
/// 段の順: 基本の勝者 → 単独勝利の上書き → 勝者が空なら詰め直し → 追加勝利 → 弁護士 → 確定敗北。
/// 全滅 (None) と廃村 (Draw) では基本の勝者より後の段を飛ばす。
/// </summary>
public static class WinnerResolver
{
    public sealed class Context
    {
        /// <summary>終了判定で決まった、上書き前の勝者</summary>
        public readonly CustomWinner Original;
        public readonly GameOverReason Reason;
        /// <summary>単独勝利の上書きが 1 つでも起きたか</summary>
        public bool Overridden;
        public Context(CustomWinner original, GameOverReason reason)
        {
            Original = original;
            Reason = reason;
        }
    }

    /// <summary>単独勝利の上書き。並びの後ろほど強い (後の物が前の勝者をリセットする)。</summary>
    private static readonly Action<Context>[] SoloOverrides =
    [
        FoxSpiritOverride,
        GodOverride,
        LoversOverride,
        DarkHideOrNeutralBakeryOverride,
    ];

    /// <summary>追加勝利。各人に対して、この並びで調べる。</summary>
    private static readonly Action<PlayerControl>[] AdditionalWinners =
    [
        LoversAdditionalWinner,
        RoleAdditionalWinner,
        ArchenemyAdditionalWinner,
    ];

    public static void Resolve(GameOverReason reason)
    {
        var ctx = new Context(CustomWinnerHolder.WinnerTeam, reason);

        AddBaseTeamWinners(ctx.Original);
        if (ctx.Original is CustomWinner.Draw or CustomWinner.None) return;

        foreach (var apply in SoloOverrides) apply(ctx);

        // 上書きが無く、勝者のチームはそのままで、誰も入っていない時だけ詰め直す
        if (!ctx.Overridden && CustomWinnerHolder.WinnerTeam == ctx.Original && CustomWinnerHolder.WinnerIds.Count == 0)
            AddBaseTeamWinners(ctx.Original);

        foreach (var pc in Main.AllPlayerControls)
            foreach (var check in AdditionalWinners) check(pc);

        //弁護士且つ追跡者
        Lawyer.EndGameCheck();

        //確定敗北陣営
        foreach (var pc in Main.AllPlayerControls)
        {
            if (pc.Is(CustomRoles.ChainShifterAddon))
            {
                CustomWinnerHolder.WinnerIds.Remove(pc.PlayerId);
                CustomWinnerHolder.CantWinPlayerIds.Add(pc.PlayerId);
            }
        }
    }

    /// <summary>クルー勝利・インポスター勝利の時に、その陣営の人を勝者に入れる。</summary>
    private static void AddBaseTeamWinners(CustomWinner team)
    {
        switch (team)
        {
            case CustomWinner.Crewmate:
                // 切断した人も入れる
                foreach (var pc in PlayerControl.AllPlayerControls)
                {
                    if (pc.Is(CustomRoleTypes.Crewmate) && !pc.Is(CustomRoles.Lovers)
                        && !(pc.Is(CustomRoles.Bakery) && Bakery.IsNeutral(pc)) && !pc.Is(CustomRoles.Archenemy))
                        CustomWinnerHolder.WinnerIds.Add(pc.PlayerId);
                }
                break;
            case CustomWinner.Impostor:
                // エゴイストが勝つ時は、CheckWin の中で勝者がエゴイストに置き換わる
                if (Egoist.CheckWin()) break;
                foreach (var pc in Main.AllPlayerControls)
                {
                    if ((pc.Is(CustomRoleTypes.Impostor) || pc.Is(CustomRoleTypes.Madmate))
                        && !pc.Is(CustomRoles.Lovers) && !pc.Is(CustomRoles.Archenemy) && !pc.Is(CustomRoles.MadJester))
                        CustomWinnerHolder.WinnerIds.Add(pc.PlayerId);
                }
                break;
        }
    }

    private static void SetSoloWinner(Context ctx, CustomWinner winner, CustomRoles role)
    {
        CustomWinnerHolder.ResetAndSetWinner(winner);
        ctx.Overridden = true;
        foreach (var pc in Main.AllPlayerControls)
            if (pc.Is(role) && pc.IsAlive()) CustomWinnerHolder.WinnerIds.Add(pc.PlayerId);
    }

    private static void FoxSpiritOverride(Context ctx)
    {
        if (FoxSpirit.CheckWin() && !ctx.Reason.Equals(GameOverReason.ImpostorsBySabotage))
            SetSoloWinner(ctx, CustomWinner.FoxSpirit, CustomRoles.FoxSpirit);
    }

    private static void GodOverride(Context ctx)
    {
        if (God.CheckWin())
            SetSoloWinner(ctx, CustomWinner.God, CustomRoles.God);
    }

    private static void LoversOverride(Context ctx)
    {
        if (Lovers.playersList.Count > 0 && Lovers.playersList.ToArray().All(p => p.IsAlive())
            && !ctx.Reason.Equals(GameOverReason.CrewmatesByTask) && !LoversWinAdditionally)
            SetSoloWinner(ctx, CustomWinner.Lovers, CustomRoles.Lovers);
    }

    private static bool LoversWinAdditionally => Lovers.LoversAddWin.GetBool() || PlatonicLover.AddWin;

    /// <summary>DarkHide と闇パン屋は、最初に当たった人だけが勝者をリセットし、2 人目からは勝者に足す。</summary>
    private static void DarkHideOrNeutralBakeryOverride(Context ctx)
    {
        var original = ctx.Original;
        var bySabotage = ctx.Reason.Equals(GameOverReason.ImpostorsBySabotage);
        var byTask = ctx.Reason.Equals(GameOverReason.CrewmatesByTask);
        var triggered = false;
        foreach (var pc in PlayerControl.AllPlayerControls)
        {
            CustomWinner winner;
            if (pc.Is(CustomRoles.DarkHide) && !pc.Data.IsDead
                && ((original == CustomWinner.Impostor && !bySabotage) || original == CustomWinner.DarkHide
                    || (original == CustomWinner.Crewmate && !byTask && ((DarkHide)pc.GetRoleClass()).IsWinKill)))
                winner = CustomWinner.DarkHide;
            else if (pc.Is(CustomRoles.Bakery) && Bakery.IsNeutral(pc) && pc.IsAlive()
                && ((original == CustomWinner.Impostor && !bySabotage) || original == CustomWinner.NBakery
                    || (original == CustomWinner.Crewmate && !byTask)))
                winner = CustomWinner.NBakery;
            else continue;

            if (!triggered)
            {
                CustomWinnerHolder.ResetAndSetWinner(winner);
                ctx.Overridden = true;
                triggered = true;
            }
            else
            {
                CustomWinnerHolder.WinnerTeam = winner;
            }
            CustomWinnerHolder.WinnerIds.Add(pc.PlayerId);
        }
    }

    private static void LoversAdditionalWinner(PlayerControl pc)
    {
        if (pc.Is(CustomRoles.Lovers) && pc.IsAlive() && LoversWinAdditionally)
        {
            CustomWinnerHolder.WinnerIds.Add(pc.PlayerId);
            CustomWinnerHolder.AdditionalWinnerRoles.Add(CustomRoles.Lovers);
        }
    }

    private static void RoleAdditionalWinner(PlayerControl pc)
    {
        if (pc.GetRoleClass() is not IAdditionalWinner additionalWinner) return;
        var winnerRole = pc.GetCustomRole();
        if (additionalWinner.CheckWin(ref winnerRole))
        {
            CustomWinnerHolder.WinnerIds.Add(pc.PlayerId);
            CustomWinnerHolder.AdditionalWinnerRoles.Add(winnerRole);
        }
    }

    private static void ArchenemyAdditionalWinner(PlayerControl pc)
    {
        if (Duelist.ArchenemyCheckWin(pc))
        {
            CustomWinnerHolder.WinnerIds.Add(pc.PlayerId);
            CustomWinnerHolder.AdditionalWinnerRoles.Add(CustomRoles.Archenemy);
        }
    }
}
