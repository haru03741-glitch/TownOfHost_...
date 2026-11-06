using System.Collections.Generic;
using UnityEngine;
using TownOfHostY.Roles.Core;
using static TownOfHostY.Options;
using TownOfHostY.Attributes;

namespace TownOfHostY.Roles.AddOns.Common;

public static class Autopsy
{
    public static readonly AddOnInfo Info = new(
        CustomRoles.Autopsy,
        AddOnGroup.Buff,
        "#80ffdd",
        "Ａ",
        setupOptions: SetupCustomOption,
        onAdd: Add);

    private static readonly int Id = (int)offsetId.AddonBuff + 400;
    private static List<byte> playerIdList = new();

    public static void SetupCustomOption()
    {
        SetupRoleOptions(Id, TabGroup.Addons, CustomRoles.Autopsy);
    }
    [GameModuleInitializer]
    public static void Init()
    {
        playerIdList = new();
    }
    public static void Add(byte playerId)
    {
        if (!playerIdList.Contains(playerId))
            playerIdList.Add(playerId);
    }
    public static bool IsEnable => playerIdList.Count > 0;
    public static bool IsThisRole(byte playerId) => playerIdList.Contains(playerId);

}