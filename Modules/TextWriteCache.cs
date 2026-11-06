using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using TMPro;

namespace TownOfHostL.Modules;

// 毎フレーム同じ文字を TMP に書き込むと、そのたびに文字列がゲーム側のヒープへ写され (GC のゴミになる)、
// 呼び出しもゲーム側を通る。前回書いた文字列と、TMP が持っている文字列の場所を覚えておき、
// どちらも変わっていなければ書き込みを省く。ほかの処理が書き換えた時は場所が変わるので書き直す。
public static class TextWriteCache
{
    struct Entry
    {
        public string Text;
        public IntPtr Native;
    }

    static readonly Dictionary<IntPtr, Entry> Entries = new();
    static int textOffset = -1;      // TMP_Text.m_text の位置 (0 = 使えない → 常に書く)
    static int cachedPtrOffset = -1; // UnityEngine.Object.m_CachedPtr の位置

    static int TextOffset
    {
        get
        {
            if (textOffset < 0) textOffset = ResolveOffset(Il2CppClassPointerStore<TMP_Text>.NativeClassPtr, "m_text");
            return textOffset;
        }
    }

    static int CachedPtrOffset
    {
        get
        {
            if (cachedPtrOffset < 0) cachedPtrOffset = ResolveOffset(Il2CppClassPointerStore<UnityEngine.Object>.NativeClassPtr, "m_CachedPtr");
            return cachedPtrOffset;
        }
    }

    static int ResolveOffset(IntPtr klass, string name)
    {
        try
        {
            IntPtr field = klass == IntPtr.Zero ? IntPtr.Zero : IL2CPP.il2cpp_class_get_field_from_name(klass, name);
            uint off = field == IntPtr.Zero ? 0 : IL2CPP.il2cpp_field_get_offset(field);
            return off is 0 or uint.MaxValue ? 0 : (int)off;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Unity 側の本体が生きているか (破棄済みなら false)。ゲーム側を呼ばずに調べる。</summary>
    public static bool IsAlive(UnityEngine.Object obj)
    {
        if (obj is null) return false;
        IntPtr p = obj.Pointer;
        if (p == IntPtr.Zero) return false;
        int off = CachedPtrOffset;
        if (off == 0) return obj != null;
        return Marshal.ReadIntPtr(p, off) != IntPtr.Zero;
    }

    /// <summary>tmp.text = value と同じ。前回と同じ内容でほかから書き換えられてもいなければ何もしない。</summary>
    public static void SetText(TMP_Text tmp, string value)
    {
        value ??= "";
        IntPtr p = tmp.Pointer;
        int off = TextOffset;
        if (off == 0 || p == IntPtr.Zero)
        {
            tmp.text = value;
            return;
        }
        IntPtr current = Marshal.ReadIntPtr(p, off);
        if (Entries.TryGetValue(p, out var e) && e.Native == current && current != IntPtr.Zero && string.Equals(e.Text, value, StringComparison.Ordinal))
            return;
        tmp.text = value;
        if (Entries.Count > 512) Entries.Clear(); // 破棄された TMP の分が溜まり続けないように
        Entries[p] = new Entry { Text = value, Native = Marshal.ReadIntPtr(p, off) };
    }
}
