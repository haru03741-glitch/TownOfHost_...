using System;
using HarmonyLib;
using Hazel;

namespace TownOfHostY
{
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.Serialize))]
    class GameManagerSerializeFix
    {
        // 本編の Serialize の置き換え。中身を書けた部品があった時だけ true を返し、本体が空のデータ電文を出さない
        public static bool Prefix(GameManager __instance, [HarmonyArgument(0)] MessageWriter writer, [HarmonyArgument(1)] bool initialState, ref bool __result)
        {
            if (__instance == null || __instance.LogicComponents == null || writer == null)
            {
                Logger.Error("GameManager / LogicComponents / MessageWriter が null です", "GameManagerSerializeFix");
                __result = false;
                return false;
            }

            bool flag = false;
            for (int index = 0; index < __instance.LogicComponents.Count; ++index)
            {
                GameLogicComponent logicComponent = __instance.LogicComponents[index];
                if (logicComponent == null) continue;
                if (!initialState && !logicComponent.IsDirty) continue;

                writer.StartMessage((byte)index);
                bool hasBody;
                try
                {
                    hasBody = logicComponent.Serialize(writer);
                }
                catch (Exception ex)
                {
                    Logger.Error($"LogicComponent[{index}].Serialize で例外: {ex.Message}", "GameManagerSerializeFix");
                    Logger.Exception(ex, "GameManagerSerializeFix");
                    hasBody = false;
                }

                if (hasBody)
                {
                    flag = true;
                    writer.EndMessage();
                }
                else writer.CancelMessage();

                logicComponent.ClearDirtyFlag();
            }

            __instance.ClearDirtyBits();
            __result = flag;
            return false;
        }
    }
}
