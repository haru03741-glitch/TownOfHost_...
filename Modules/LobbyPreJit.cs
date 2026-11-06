using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using TownOfHostL.Roles.Core;

namespace TownOfHostL.Modules;

// 試合で初めて通る処理は、その場で JIT コンパイルされるので試合開始の瞬間に 100ms 級の引っかかりになる
// (4 人の実測で開始直後に約 1,400 関数・300ms)。ロビーで待っている間に、メインスレッドで 1 フレームあたり
// 数 ms ずつ先にコンパイルしておく。
// 裏スレッドでコンパイルすると、型の静的初期化子がメインスレッド以外で走り、ゲーム側を呼ぶ初期化子で
// 落ちる恐れがあるため、あえてメインスレッドで少しずつ行う。
// 対象は役職以外のコードすべてと、ロビーで有効になっている役職のクラス (中のラムダ等を含む)。
// 無効な役職のクラスも、全役職をなめる共通処理から呼ばれる静的関数だけは先にコンパイルする。
// 型の静的初期化子もここで (メインスレッドで) 走らせる。試合の状態を読む初期化子は無いことを確認済み。
public static class LobbyPreJit
{
    const float StartDelaySec = 5f;   // ロビーに入ってから始めるまで
    const double BudgetMs = 2.0;      // 1 フレームあたりの持ち時間
    const float RoleRescanSec = 2f;   // 有効な役職の見直し間隔

    static readonly Queue<(Type type, bool staticOnly)> Pending = new();
    static readonly HashSet<Type> Queued = new();       // 全部の関数を積んだ型
    static readonly HashSet<Type> StaticQueued = new(); // 静的関数だけ積んだ型
    static readonly HashSet<Type> CctorDone = new();
    static readonly HashSet<CustomRoles> RolesQueued = new();
    static Dictionary<Type, CustomRoles> roleOfType;
    static Type[] allTypes;
    static bool baseQueued;
    static float lobbySince = -1f;
    static float nextRoleScan;
    static Type currentType;
    static MethodBase[] currentMethods;
    static int currentIndex;

    static int compiled, failed, cctors;
    static double totalMs;
    static bool reported = true;

    public static void Tick()
    {
        try
        {
            if (!GameStates.IsLobby)
            {
                lobbySince = -1f;
                return;
            }
            float now = UnityEngine.Time.unscaledTime;
            if (lobbySince < 0f) lobbySince = now;
            if (now - lobbySince < StartDelaySec) return;

            if (!baseQueued) QueueBase();
            if (now >= nextRoleScan)
            {
                nextRoleScan = now + RoleRescanSec;
                QueueEnabledRoles();
            }
            Step();
        }
        catch (Exception ex)
        {
            Logger.Error($"LobbyPreJit: {ex.Message}", nameof(LobbyPreJit));
            Pending.Clear();
            currentMethods = null;
        }
    }

    static void QueueBase()
    {
        baseQueued = true;
        roleOfType = new();
        foreach (var kvp in CustomRoleManager.AllRolesInfo)
            if (kvp.Value?.ClassType != null) roleOfType[kvp.Value.ClassType] = kvp.Key;

        allTypes = typeof(Main).Assembly.GetTypes();
        foreach (var t in allTypes)
            // 役職のクラスは有効なものだけ後で全部積む。ただし役職情報の登録で作られるラムダ (<>c) は全役職分を呼ばれるので全部積む
            Enqueue(t, staticOnly: OwnerRole(t, out _) && !t.Name.StartsWith("<>c", StringComparison.Ordinal));
    }

    static void QueueEnabledRoles()
    {
        if (roleOfType == null) return;
        List<Type> add = null;
        foreach (var t in allTypes)
        {
            if (Queued.Contains(t) || !OwnerRole(t, out var role)) continue;
            if (!RolesQueued.Contains(role) && !role.IsEnable()) continue;
            (add ??= new()).Add(t);
        }
        if (add == null) return;
        foreach (var t in add)
        {
            if (OwnerRole(t, out var role)) RolesQueued.Add(role);
            Enqueue(t, staticOnly: false);
        }
    }

    // t が役職のクラスそのもの、またはその中に入れ子になった型 (ラムダ・イテレーター) なら true
    static bool OwnerRole(Type t, out CustomRoles role)
    {
        for (var cur = t; cur != null; cur = cur.DeclaringType)
            if (roleOfType.TryGetValue(cur, out role)) return true;
        role = default;
        return false;
    }

    static void Enqueue(Type t, bool staticOnly)
    {
        if (t.ContainsGenericParameters || t.IsInterface) return;
        if (staticOnly ? Queued.Contains(t) || !StaticQueued.Add(t) : !Queued.Add(t)) return;
        Pending.Enqueue((t, staticOnly));
        reported = false;
    }

    static void Step()
    {
        long start = Stopwatch.GetTimestamp();
        long budget = (long)(BudgetMs * Stopwatch.Frequency / 1000.0);
        while (Stopwatch.GetTimestamp() - start < budget)
        {
            if (currentMethods == null || currentIndex >= currentMethods.Length)
            {
                if (Pending.Count == 0)
                {
                    Report();
                    break;
                }
                var (type, staticOnly) = Pending.Dequeue();
                currentType = type;
                if (type.TypeInitializer != null && CctorDone.Add(type))
                {
                    try { RuntimeHelpers.RunClassConstructor(type.TypeHandle); cctors++; }
                    catch { failed++; }
                }
                currentMethods = GetMethods(type, staticOnly, skipStatic: !staticOnly && StaticQueued.Contains(type));
                currentIndex = 0;
                continue;
            }
            var m = currentMethods[currentIndex++];
            try
            {
                RuntimeHelpers.PrepareMethod(m.MethodHandle);
                compiled++;
            }
            catch
            {
                failed++;
            }
        }
        totalMs += (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    static MethodBase[] GetMethods(Type t, bool staticOnly, bool skipStatic)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var list = new List<MethodBase>();
        try
        {
            foreach (var m in t.GetMethods(flags))
            {
                if (staticOnly && !m.IsStatic || skipStatic && m.IsStatic) continue;
                if (CanPrepare(m)) list.Add(m);
            }
            if (!staticOnly)
                foreach (var c in t.GetConstructors(flags))
                    if (!c.IsStatic && CanPrepare(c)) list.Add(c);
        }
        catch { }
        return list.ToArray();
    }

    static bool CanPrepare(MethodBase m)
    {
        if (m.IsAbstract || m.ContainsGenericParameters) return false;
        var impl = m.MethodImplementationFlags;
        if ((impl & (MethodImplAttributes.InternalCall | MethodImplAttributes.Runtime)) != 0) return false;
        if ((m.Attributes & MethodAttributes.PinvokeImpl) != 0) return false;
        return true;
    }

    static void Report()
    {
        if (reported) return;
        reported = true;
        Logger.Info($"compiled={compiled} cctors={cctors} failed={failed} mainThreadMs={totalMs:F0} roles={RolesQueued.Count}", nameof(LobbyPreJit));
    }
}
