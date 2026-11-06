using HarmonyLib;
using AmongUs.GameOptions;

namespace TownOfHostL.Patches
{
    [HarmonyPatch(typeof(LogicOptions), nameof(LogicOptions.GetKillDistance))]
    class LogicOptionsGetKillDistancePatch
    {
        // 通常は本編の値 (キル距離の設定) をそのまま使い、本編側が例外を出した時だけ短距離に倒す
        public static bool Prefix(LogicOptions __instance, ref float __result)
        {
            if (__instance != null) return true;
            __result = 1.0f;
            return false;
        }

        public static System.Exception Finalizer(System.Exception __exception, ref float __result)
        {
            if (__exception == null) return null;
            Logger.Exception(__exception, "LogicOptionsGetKillDistancePatch");
            __result = 1.0f;
            return null;
        }
    }
}
