using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;

namespace TownOfHostL.Patches
{
    // マネージドの IEnumerator を StartCoroutine に渡すと Il2CppManagedEnumerator で包まれ、その包みは
    // コルーチンが終わった後も GCHandle で生き残る。包みが中身の IEnumerator を握ったままだと、
    // 状態機械が閉包ごと掴んでいたもの (前の試合の HudManager / IntroCutscene / EndGameManager など) が回収されず、
    // 試合のたびに積み上がる。包みを作る時点で中身を 1 枚挟み、終わったら中身を手放させる。
    // MoveNext 側へのパッチは il2cpp からの呼び出しを素通りするので、管理コードから呼ばれる生成側で挟む。
    [HarmonyPatch(typeof(Il2CppManagedEnumerator), MethodType.Constructor, typeof(IEnumerator))]
    internal static class FinishedCoroutineReleasePatch
    {
        private static bool logged;

        public static void Prefix(ref IEnumerator __0)
        {
            if (__0 is null or ReleasingEnumerator) return;
            __0 = new ReleasingEnumerator(__0);
        }

        private sealed class ReleasingEnumerator : IEnumerator
        {
            private IEnumerator inner;

            public ReleasingEnumerator(IEnumerator inner) => this.inner = inner;

            public object Current => inner?.Current;

            public bool MoveNext()
            {
                if (inner == null) return false;

                try
                {
                    if (inner.MoveNext()) return true;
                }
                catch
                {
                    // 例外で止まったコルーチンも Unity からは二度と呼ばれないので、ここで手放す
                    inner = null;
                    throw;
                }

                inner = null;

                if (!logged)
                {
                    logged = true;
                    Logger.Info("first finished coroutine released", "CoroutineRelease");
                }

                return false;
            }

            public void Reset() => inner?.Reset();
        }
    }
}
