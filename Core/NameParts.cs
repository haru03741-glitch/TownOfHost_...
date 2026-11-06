using System.Text;
using UnityEngine;

using TownOfHostL;
using TownOfHostL.Roles.AddOns.Common;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Impostor;

namespace TownOfHostL.Core;

/// <summary>
/// 名前の表示の部品 (マーク・サフィックス・下段・死因・カモフラで隠すか) を組み立てる。
/// 客へ送る名前 (Utils.NotifyRoles)・ホストの画面の名前 (FixedUpdatePatch)・ホストの会議画面 (MeetingHudPatch) が同じ部品を使う。
/// ここでは送らず、画面にも書かない。並べ方と書き込み先は呼ぶ側が決める。
/// seen を null で渡すのは「seer 自身の名前」を意味する (役職のフックにもそのまま null を渡す)。
/// </summary>
public static class NameParts
{
    /// <summary>名前の後ろに付けるマーク</summary>
    public static StringBuilder AppendMark(StringBuilder sb, PlayerControl seer, PlayerControl seen, bool isMeeting)
    {
        sb.Append(seer.GetRoleClass()?.GetMark(seer, seen, isMeeting));
        sb.Append(CustomRoleManager.GetMarkOthers(seer, seen, isMeeting));
        sb.Append(Lovers.GetMark(seer, seen));
        return sb;
    }
    /// <summary>自分の名前に付けるマーク。通報できなかった時の印を足す</summary>
    public static StringBuilder AppendSelfMark(StringBuilder sb, PlayerControl seer, bool isMeeting)
    {
        AppendMark(sb, seer, null, isMeeting);
        return AppendReportMark(sb, seer);
    }
    /// <summary>通報できなかった直後の印 (自分の名前にだけ付ける)</summary>
    public static StringBuilder AppendReportMark(StringBuilder sb, PlayerControl seer)
    {
        if (ReportDeadBodyPatch.DontReportMarkList.Contains(seer.PlayerId))
            sb.Append(Utils.ColorString(Palette.Orange, "◀×"));
        return sb;
    }

    /// <summary>
    /// 名前の次の行に付けるサフィックス。Management と死体の矢印は自分の名前にしか出ない。
    /// </summary>
    public static StringBuilder AppendSuffix(StringBuilder sb, PlayerControl seer, PlayerControl seen, bool isMeeting)
    {
        sb.Append(seer.GetRoleClass()?.GetSuffix(seer, seen, isMeeting));
        sb.Append(CustomRoleManager.GetSuffixOthers(seer, seen, isMeeting));
        if (seer.Is(CustomRoles.Management))
            sb.Append(Management.GetSuffix(seer, seen, isMeeting));
        if (!isMeeting)
            sb.Append(TargetDeadArrow.GetDeadBodiesArrow(seer, seen ?? seer));
        return sb;
    }
    /// <summary>他人 (またはホストの画面での自分) の名前の下に並べる物。役職に関わらない下段の後にサフィックス</summary>
    public static StringBuilder AppendSeenSuffix(StringBuilder sb, PlayerControl seer, PlayerControl seen, bool isMeeting)
    {
        sb.Append(CustomRoleManager.GetLowerTextOthers(seer, seen, isMeeting));
        return AppendSuffix(sb, seer, seen, isMeeting);
    }
    /// <summary>客へ送る自分の名前の最下段 (ホストは HUD に出すので使わない)</summary>
    public static StringBuilder AppendSelfLower(StringBuilder sb, PlayerControl seer, bool isMeeting)
    {
        sb.Append(VentEnterTask.GetLowerText(seer, isForMeeting: isMeeting));
        sb.Append(seer.GetRoleClass()?.GetLowerText(seer, isForMeeting: isMeeting));
        sb.Append(CustomRoleManager.GetLowerTextOthers(seer, isForMeeting: isMeeting));
        return sb;
    }

    /// <summary>seer に seen の死因が見えるなら「(死因)」、見えなければ空</summary>
    public static string DeathReason(PlayerControl seer, PlayerControl seen)
        => seer.KnowDeathReason(seen)
            ? $"({Utils.ColorString(Utils.GetRoleColor(CustomRoles.Doctor), Utils.GetVitalText(seen.PlayerId))})"
            : "";

    /// <summary>
    /// 他人の名前をカモフラで隠すか。会議中は隠さない。
    /// 隠し方 (文字の大きさを 0 にする書き方) は書き込み先で違うので、呼ぶ側が決める。
    /// </summary>
    public static bool HideSeenName(bool isMeeting)
    {
        if (isMeeting) return false;
        if (Utils.IsActive(SystemTypes.Comms) && Options.CommsCamouflage.GetBool() && !Options.IsSyncColorMode) return true;
        return EvilDyer.IsColorCamouflage;
    }
}
