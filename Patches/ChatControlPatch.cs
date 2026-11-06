using AmongUs.Data;
using HarmonyLib;
using UnityEngine;

namespace TownOfHostL
{
    // 部屋はクイックチャット専用かどうかを作る時に決めるので、部屋を立てる前にフリーチャットへ直す。
    // クイックチャット専用の部屋でホストがフリーチャット (案内やコマンドの返事) を送ると、公式サーバーはホストを切断する。
    [HarmonyPatch(typeof(InnerNet.InnerNetClient), nameof(InnerNet.InnerNetClient.HostGame))]
    class HostGameChatModePatch
    {
        public static void Prefix()
        {
            if (DataManager.Settings.Multiplayer.ChatMode != InnerNet.QuickChatModes.QuickChatOnly) return;
            DataManager.Settings.Multiplayer.ChatMode = InnerNet.QuickChatModes.FreeChatOrQuickChat;
            Logger.Info("クイックチャット専用の設定をフリーチャットに直してから部屋を立てます", "HostGameChatMode");
        }
    }

    [HarmonyPatch(typeof(ChatController), nameof(ChatController.Update))]
    class ChatControllerUpdatePatch
    {
        public static int CurrentHistorySelection = -1;
        public static void Prefix()
        {
            if (AmongUsClient.Instance.AmHost && DataManager.Settings.Multiplayer.ChatMode == InnerNet.QuickChatModes.QuickChatOnly)
                DataManager.Settings.Multiplayer.ChatMode = InnerNet.QuickChatModes.FreeChatOrQuickChat; //コマンドを打つためにホストのみ常時フリーチャット開放
        }
        public static void Postfix(ChatController __instance)
        {
            if (!__instance.freeChatField.textArea.hasFocus) return;
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.C))
                ClipboardHelper.PutClipboardString(__instance.freeChatField.textArea.text);
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.V))
                __instance.freeChatField.textArea.SetText(__instance.freeChatField.textArea.text + GUIUtility.systemCopyBuffer);
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.X))
            {
                ClipboardHelper.PutClipboardString(__instance.freeChatField.textArea.text);
                __instance.freeChatField.textArea.SetText("");
            }
            if (Input.GetKeyDown(KeyCode.UpArrow) && ChatCommands.ChatHistory.Count > 0)
            {
                CurrentHistorySelection = Mathf.Clamp(--CurrentHistorySelection, 0, ChatCommands.ChatHistory.Count - 1);
                __instance.freeChatField.textArea.SetText(ChatCommands.ChatHistory[CurrentHistorySelection]);
            }
            if (Input.GetKeyDown(KeyCode.DownArrow) && ChatCommands.ChatHistory.Count > 0)
            {
                CurrentHistorySelection++;
                if (CurrentHistorySelection < ChatCommands.ChatHistory.Count)
                    __instance.freeChatField.textArea.SetText(ChatCommands.ChatHistory[CurrentHistorySelection]);
                else __instance.freeChatField.textArea.SetText("");
            }
        }
    }
}