using System;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx.Logging;
using Il2CppInterop.Runtime;

namespace TownOfHostL.Modules;

// Based on https://github.com/Dolly1016/Nebula-Public (NebulaPluginNova/Utilities/IncrementalGcInvalidator.cs, GPL-3.0)
// and https://github.com/waffle-ful/Aeterna-End-K-not (Modules/IncrementalGcInvalidator.cs, GPL-3.0)
//
// il2cpp の incremental GC は、マーク途中のオブジェクトへ write barrier を通さずに参照を書くと、
// 書いた先がまだ生きているのに回収してしまう。元の Il2CppInterop.Runtime は 3 か所
// (FieldWriteWbarrierStub・Il2CppReferenceArray<T>.StoreValue・Il2CppStringArray の setter) を barrier なしで書く。
// 回収された先を触るとプロセスごと落ち、try/catch では止められない。
//
// 配布物の Il2CppInterop.Runtime.dll は tools/InteropWbPatch でこの 3 か所を barrier 経由に直してあり、
// 目印として IL2CPP.WbStore を持つ。それが載った 64bit プロセスでは incremental を ON のまま残す
// (定期の収集が数 ms ずつに分かれ、1 回の停止が短くなる)。それ以外の環境では incremental を切る。
//
// 切り方: il2cpp_gc_is_incremental が読むグローバル変数を機械語から探して 0 を書く。
// export → jmp (E9) を辿る → 先頭 24 バイトから mov eax,[rip+disp32] (8B 05, x64) か mov eax,[abs32] (A1, x86) を探す
// → 無ければ最初の call (E8) の先で同じことをする。
// 無効なアドレスの読み書きは catch できないので、触る前に必ず VirtualQuery でページを確かめ、
// 書いた後に il2cpp_gc_is_incremental が false になったかを見て、ならなければ元に戻す。
public static class IncrementalGcGate
{
    private const int ScanBytes = 24;
    private const int MaxJmpHops = 5;

    private const uint MemCommit = 0x1000;
    private const uint PageGuard = 0x100;
    private const uint Readable = 0x02 | 0x04 | 0x08 | 0x20 | 0x40 | 0x80;
    private const uint Writable = 0x04 | 0x08 | 0x40 | 0x80;

    // Main.Logger より先に呼ぶので BepInEx のログへ直接出す
    private static readonly ManualLogSource log = BepInEx.Logging.Logger.CreateLogSource("TOH_L.IncrementalGc");

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string moduleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr module, string procName);

    [DllImport("kernel32.dll")]
    private static extern UIntPtr VirtualQuery(IntPtr address, out MemoryBasicInformation buffer, UIntPtr length);

    [DllImport("GameAssembly", CallingConvention = CallingConvention.Cdecl, EntryPoint = "il2cpp_gc_is_incremental")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool IsIncremental();

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryBasicInformation
    {
        public IntPtr BaseAddress;
        public IntPtr AllocationBase;
        public uint AllocationProtect;
        public IntPtr RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    // Load の最初に一度だけ呼ぶ。ここで例外が漏れると mod が読み込まれないので、全体を囲む
    public static void Apply()
    {
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

            bool incremental = IsIncremental();
            if (Environment.Is64BitProcess && HasBarrierRuntime())
            {
                log.LogInfo(incremental
                    ? "incremental GC: on (write-barrier Il2CppInterop.Runtime)"
                    : "incremental GC: off before load (GC_DISABLE_INCREMENTAL in doorstop_config.ini?)");
                return;
            }
            if (!incremental)
            {
                log.LogInfo("incremental GC: already off");
                return;
            }
            log.LogInfo($"incremental GC: turning off (64bit={Environment.Is64BitProcess}, write-barrier runtime={HasBarrierRuntime()}): {Disable()}");
        }
        catch (Exception ex)
        {
            log.LogWarning($"incremental GC: check failed, left as is ({ex.GetType().Name}: {ex.Message})");
        }
    }

    private static bool HasBarrierRuntime()
        => typeof(IL2CPP).GetMethod("WbStore", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(IntPtr), typeof(IntPtr) }, null) != null;

    private static string Disable()
    {
        IntPtr flag = FindFlag();
        if (flag == IntPtr.Zero) return "FAILED (flag not found)";
        if (!HasProtection(flag, 4, Writable)) return $"FAILED (0x{flag.ToInt64():X} is not writable)";

        int original = Marshal.ReadInt32(flag);
        // 読んだ値と il2cpp_gc_is_incremental の答えが食い違うなら別の変数を掴んでいる
        if ((original != 0) != IsIncremental()) return $"FAILED (0x{flag.ToInt64():X} holds {original}, not the flag)";

        Marshal.WriteInt32(flag, 0);
        if (IsIncremental())
        {
            Marshal.WriteInt32(flag, original);
            return "FAILED (still on after write, restored)";
        }
        return $"OK (0x{flag.ToInt64():X}: {original} -> 0)";
    }

    private static IntPtr FindFlag()
    {
        IntPtr module = GetModuleHandleW("GameAssembly.dll");
        if (module == IntPtr.Zero) return IntPtr.Zero;
        IntPtr func = FollowJmp(GetProcAddress(module, "il2cpp_gc_is_incremental"));
        if (func == IntPtr.Zero) return IntPtr.Zero;

        IntPtr flag = FindGlobalRead(func);
        if (flag != IntPtr.Zero) return flag;

        IntPtr callee = FindFirstCall(func);
        return callee == IntPtr.Zero ? IntPtr.Zero : FindGlobalRead(FollowJmp(callee));
    }

    private static IntPtr FollowJmp(IntPtr addr)
    {
        for (int i = 0; i < MaxJmpHops; i++)
        {
            if (!HasProtection(addr, 5, Readable)) return IntPtr.Zero;
            if (Marshal.ReadByte(addr) != 0xE9) break;
            addr = addr + 5 + Marshal.ReadInt32(addr, 1);
        }
        return addr;
    }

    private static IntPtr FindFirstCall(IntPtr func)
    {
        if (!HasProtection(func, ScanBytes, Readable)) return IntPtr.Zero;
        for (int i = 0; i < ScanBytes - 4; i++)
        {
            if (Marshal.ReadByte(func, i) == 0xE8)
                return func + i + 5 + Marshal.ReadInt32(func, i + 1);
        }
        return IntPtr.Zero;
    }

    private static IntPtr FindGlobalRead(IntPtr func)
    {
        if (!HasProtection(func, ScanBytes, Readable)) return IntPtr.Zero;
        for (int i = 0; i < ScanBytes - 6; i++)
        {
            byte op = Marshal.ReadByte(func, i);
            if (op == 0x8B && Marshal.ReadByte(func, i + 1) == 0x05)
            {
                int disp = Marshal.ReadInt32(func, i + 2);
                // x64 は次の命令の先頭からの相対、x86 は絶対アドレス
                return Environment.Is64BitProcess ? func + i + 6 + disp : (IntPtr)disp;
            }
            if (!Environment.Is64BitProcess && op == 0xA1)
                return (IntPtr)Marshal.ReadInt32(func, i + 1);
        }
        return IntPtr.Zero;
    }

    // 先頭と末尾がページをまたぐことがあるので両端を見る
    private static bool HasProtection(IntPtr addr, int length, uint mask)
        => addr != IntPtr.Zero && PageHas(addr, mask) && PageHas(addr + length - 1, mask);

    private static bool PageHas(IntPtr addr, uint mask)
    {
        if (VirtualQuery(addr, out var mbi, (UIntPtr)(uint)Marshal.SizeOf<MemoryBasicInformation>()) == UIntPtr.Zero) return false;
        return mbi.State == MemCommit && (mbi.Protect & PageGuard) == 0 && (mbi.Protect & mask) != 0;
    }
}
