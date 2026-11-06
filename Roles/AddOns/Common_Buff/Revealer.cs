using System.Collections.Generic;
using UnityEngine;
using TownOfHostL.Roles.Core;
using TownOfHostL.Attributes;
using static TownOfHostL.Options;

namespace TownOfHostL.Roles.AddOns.Common;

public static class Revealer
{
    public static readonly AddOnInfo Info = new(
        CustomRoles.Revealer,
        AddOnGroup.Buff,
        "#e478ff",
        "Rv",
        setupOptions: SetupCustomOption,
        onAdd: Add);

    private static readonly int Id = (int)offsetId.AddonBuff + 1300;
    private static List<byte> playerIdList = new();
    private static HashSet<byte> SeenIdList = new();
    private static HashSet<byte> pendingRevealList = new();

    public static void SetupCustomOption()
    {
        SetupRoleOptions(Id, TabGroup.Addons, CustomRoles.Revealer);
    }
    [GameModuleInitializer]
    public static void Init()
    {
        playerIdList = new();
        SeenIdList = new();
        pendingRevealList = new();
    }
    public static void OnStartMeeting()
    {
        foreach (var id in pendingRevealList)
            SeenIdList.Add(id);
        pendingRevealList.Clear();
    }
    public static void Add(byte playerId)
    {
        if (!playerIdList.Contains(playerId))
            playerIdList.Add(playerId);
    }

    public static void ChangeName(PlayerControl pc)
    {
        if (!pc.Is(CustomRoles.Revealer)) return;
        pendingRevealList.Add(pc.PlayerId);

        string exiledRole = Utils.GetRoleName(pc.GetCustomRole());
        string exiledName = pc.GetRealName(true);

        if (pc == PlayerControl.LocalPlayer)
        {
            if (Main.nickName != "")
            {
                exiledName = Main.nickName;
            }
            Main.nickName = string.Format(Translator.GetString("RevealerExiledText"), exiledRole, exiledName);
        }
        else
        {
            pc.RpcSetName(string.Format(Translator.GetString("RevealerExiledText"), exiledRole, exiledName));
        }
        
        if (pc == PlayerControl.LocalPlayer)
        {
            // Main.nickName は試合をまたいで残るので、戻す前に試合が閉じても戻す
            _ = new LateTask(() => Main.nickName = exiledName, 8f, "SetName", outlivesSession: true);
        }
        else
        {
            _ = new LateTask(() => pc.RpcSetName(exiledName), 8f, "SetName");
        }
    }

    public static void OverrideDisplayRoleNameAsSeen(PlayerControl seen, bool isMeeting, ref bool enabled, ref string roleText)
    {
        if (!seen.Is(CustomRoles.Revealer) || !SeenIdList.Contains(seen.PlayerId)) return;

        enabled = true;
    }


    public static bool IsEnable => playerIdList.Count > 0;
    public static bool IsThisRole(byte playerId) => playerIdList.Contains(playerId);
}