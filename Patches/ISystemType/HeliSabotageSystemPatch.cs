using HarmonyLib;
using Hazel;
using TownOfHostY.Roles.Core;
using TownOfHostY.Roles.Core.Interfaces;

namespace TownOfHostY.Patches.ISystemType;

[HarmonyPatch(typeof(HeliSabotageSystem), nameof(HeliSabotageSystem.UpdateSystem))]
public static class HeliSabotageSystemUpdateSystemPatch
{
    public static bool Prefix(HeliSabotageSystem __instance, [HarmonyArgument(0)] PlayerControl player, [HarmonyArgument(1)] MessageReader msgReader)
    {
        byte amount;
        {
            var newReader = MessageReader.Get(msgReader);
            amount = newReader.ReadByte();
            newReader.Recycle();
        }

        if (player.GetRoleClass() is ISystemTypeUpdateHook systemTypeUpdateHook && !systemTypeUpdateHook.UpdateHeliSabotageSystem(__instance, amount))
        {
            return false;
        }
        return true;
    }
}

// 墜落サボタージュの残り時間を、設定した制限時間より長くしない。
// HeliSabotageSystem はエアシップにしか無いので、マップは確かめない。
[HarmonyPatch(typeof(HeliSabotageSystem), nameof(HeliSabotageSystem.Deteriorate))]
public static class HeliSabotageSystemPatch
{
    public static void Prefix(HeliSabotageSystem __instance)
    {
        if (!__instance.IsActive || !Options.SabotageTimeControl_Airship.GetBool()) return;

        float limit = Options.AirshipReactorTimeLimit.GetFloat();
        if (__instance.Countdown > limit) __instance.Countdown = limit;
    }
}
