using System.Collections.Generic;
using UnityEngine;
using TownOfHostY.Roles.Core;
using static TownOfHostY.Options;
using TownOfHostY.Attributes;

namespace TownOfHostY.Roles.AddOns.Common;

public static class NonReport
{
    public static readonly AddOnInfo Info = new(
        CustomRoles.NonReport,
        AddOnGroup.Debuff,
        "#883fd1",
        "Nr",
        setupOptions: SetupCustomOption,
        onAdd: Add);

    private static readonly int Id = (int)offsetId.AddonDebuff + 300;
    private static List<byte> playerIdList = new();

    public static void SetupCustomOption()
    {
        SetupRoleOptions(Id, TabGroup.Addons, CustomRoles.NonReport);
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