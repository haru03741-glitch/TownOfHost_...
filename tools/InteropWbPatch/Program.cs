// Based on https://github.com/waffle-ful/Aeterna-End-K-not (tools/InteropWbPatch, GPL-3.0)
//
// Il2CppInterop.Runtime 1.5.3 が write barrier を通さずに il2cpp の参照を書く 3 か所を、
// ゲームの il2cpp_gc_wbarrier_set_field 経由に書き換える。
//   1) IL2CPP.FieldWriteWbarrierStub の本体 (ScanMethodRefs=false で生成した interop のフィールド setter が呼ぶ)
//   2) Il2CppReferenceArray<T>.StoreValue の stind.i
//   3) Il2CppStringArray の indexer setter の stind.i
// 2) 3) はスタックに (書く先, 値) が積まれているので、新しく足す IL2CPP.WbStore(書く先, 値) の呼び出しに置き換える。
// il2cpp_gc_wbarrier_set_field は第 1 引数 (オブジェクト) を使わない (*target = value の後に GC_dirty(target)) ので 0 を渡す。
// TOHL 本体は IL2CPP.WbStore の有無で書き換え済みかを判断する (Modules/IncrementalGcGate.cs)。
//
// 使い方: dotnet run --project tools/InteropWbPatch -c Release -- <元の Il2CppInterop.Runtime.dll> <出力先>
using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: InteropWbPatch <Il2CppInterop.Runtime.dll> <output>");
    return 2;
}

using var asm = AssemblyDefinition.ReadAssembly(args[0], new ReaderParameters { InMemory = true });
var mod = asm.MainModule;
var il2cpp = mod.GetType("Il2CppInterop.Runtime.IL2CPP");
if (il2cpp.Methods.Any(m => m.Name == "WbStore"))
{
    Console.Error.WriteLine("already patched");
    return 3;
}
var setField = il2cpp.Methods.Single(m => m.Name == "il2cpp_gc_wbarrier_set_field");

// 1)
var stub = il2cpp.Methods.Single(m => m.Name == "FieldWriteWbarrierStub");
stub.Body.Instructions.Clear();
stub.Body.Variables.Clear();
stub.Body.ExceptionHandlers.Clear();
var il = stub.Body.GetILProcessor();
il.Emit(OpCodes.Ldarg_0);
il.Emit(OpCodes.Ldarg_1);
il.Emit(OpCodes.Ldarg_2);
il.Emit(OpCodes.Call, setField);
il.Emit(OpCodes.Ret);

// WbStore(IntPtr targetAddress, IntPtr value)
var wbStore = new MethodDefinition("WbStore", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, mod.TypeSystem.Void);
wbStore.Parameters.Add(new ParameterDefinition("targetAddress", ParameterAttributes.None, mod.TypeSystem.IntPtr));
wbStore.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, mod.TypeSystem.IntPtr));
il = wbStore.Body.GetILProcessor();
il.Emit(OpCodes.Ldc_I4_0);
il.Emit(OpCodes.Conv_I);
il.Emit(OpCodes.Ldarg_0);
il.Emit(OpCodes.Ldarg_1);
il.Emit(OpCodes.Call, setField);
il.Emit(OpCodes.Ret);
il2cpp.Methods.Add(wbStore);

int ReplaceStores(string typeName, string methodName)
{
    var method = mod.GetType(typeName).Methods.Single(m => m.Name == methodName);
    var stores = method.Body.Instructions.Where(i => i.OpCode == OpCodes.Stind_I).ToList();
    foreach (var s in stores)
    {
        s.OpCode = OpCodes.Call;
        s.Operand = wbStore;
    }
    return stores.Count;
}

// 2) 3)
int refArray = ReplaceStores("Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray`1", "StoreValue");
int stringArray = ReplaceStores("Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray", "set_Item");

asm.Write(args[1]);
Console.WriteLine($"patched: stub=1 refArray={refArray} stringArray={stringArray} -> {args[1]}");
return refArray == 1 && stringArray == 1 ? 0 : 4;
