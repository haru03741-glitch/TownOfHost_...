using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;
using HarmonyLib;

namespace TownOfHostY.Modules;

// Based on https://github.com/waffle-ful/Aeterna-End-K-not (Modules/PatchPhases.cs DelegateTypeCache, GPL-3.0)
// il2cpp のメソッドをパッチするたびに、Il2CppInterop は HarmonyX の DelegateTypeFactory に
// トランポリン用の delegate 型を頼む。HarmonyX 2.10 はその都度アセンブリを丸ごと作って読み込む (1 回十数 ms)。
// 型の中身は戻り値・引数・呼び出し規約だけで決まるので、同じ署名なら 1 つの動的モジュールに作った型を使い回す。
// 起動時の PatchAll の大半がこの生成なので、ここを省くと起動が数秒縮む。
public static class DelegateTypeCache
{
    private static readonly Dictionary<string, Type> types = new();
    private static ModuleBuilder module;
    private static int counter;

    // PatchAll より前に呼ぶ。入らなかったときは HarmonyX の本来の処理のまま動く
    public static void Install(Harmony harmony)
    {
        try
        {
            var target = AccessTools.Method(typeof(DelegateTypeFactory), nameof(DelegateTypeFactory.CreateDelegateType),
                new[] { typeof(Type), typeof(Type[]), typeof(CallingConvention?) });
            if (target == null) throw new MissingMethodException("DelegateTypeFactory.CreateDelegateType(Type, Type[], CallingConvention?)");
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(DelegateTypeCache), nameof(Prefix)) { priority = Priority.First });
        }
        catch (Exception ex)
        {
            Logger.Error($"delegate 型のキャッシュを入れられなかった: {ex}", "DelegateTypeCache");
        }
    }

    // 引数名は HarmonyX の CreateDelegateType と一致させること (違うとパッチの組み立てで落ちる)
    private static bool Prefix(Type returnType, Type[] argTypes, CallingConvention? convention, ref Type __result)
    {
        try
        {
            var key = new StringBuilder(returnType.AssemblyQualifiedName).Append('|');
            foreach (var t in argTypes) key.Append(t.AssemblyQualifiedName).Append(',');
            key.Append('|').Append(convention.HasValue ? ((int)convention.Value).ToString() : "-");

            lock (types)
            {
                if (!types.TryGetValue(key.ToString(), out var type))
                {
                    type = Create(returnType, argTypes, convention);
                    types[key.ToString()] = type;
                }
                __result = type;
            }
            return false;
        }
        catch (Exception ex)
        {
            Logger.Warn($"delegate 型を作れず、本来の処理に任せる: {ex.Message}", "DelegateTypeCache");
            return true;
        }
    }

    private static Type Create(Type returnType, Type[] argTypes, CallingConvention? convention)
    {
        module ??= AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("TOHLDelegateTypes"), AssemblyBuilderAccess.Run)
            .DefineDynamicModule("TOHLDelegateTypes");

        var tb = module.DefineType($"TOHLDelegate{++counter}", TypeAttributes.Sealed | TypeAttributes.Public, typeof(MulticastDelegate));
        if (convention.HasValue)
        {
            var attrCtor = typeof(UnmanagedFunctionPointerAttribute).GetConstructor(new[] { typeof(CallingConvention) });
            tb.SetCustomAttribute(new CustomAttributeBuilder(attrCtor, new object[] { convention.Value }));
        }

        var ctor = tb.DefineConstructor(MethodAttributes.RTSpecialName | MethodAttributes.HideBySig | MethodAttributes.Public,
            CallingConventions.Standard, new[] { typeof(object), typeof(IntPtr) });
        ctor.SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);

        var invoke = tb.DefineMethod("Invoke", MethodAttributes.HideBySig | MethodAttributes.Virtual | MethodAttributes.Public, returnType, argTypes);
        invoke.SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);

        return tb.CreateType();
    }
}
