using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace TownOfHostY.Attributes;

/// <summary>初期化メソッドの属性の共通の土台。アセンブリの走査を 1 回で済ませるために使う。</summary>
[AttributeUsage(AttributeTargets.Method)]
public abstract class InitializerAttributeBase : Attribute
{
    private static bool scanned;

    internal abstract void Bind(MethodInfo method);

    /// <summary>
    /// アセンブリ内の初期化メソッドを、種類 (起動時・試合ごと) を問わず 1 回の走査で集める。
    /// 以前は種類ごとに全型の全 public メソッドを走査していて、試合ごとの分は最初の試合の開始の瞬間に走っていた。
    /// 初期化メソッドは Invoke(null, null) で呼ぶので static に限られ、static メソッドだけを見ればよい。
    /// </summary>
    internal static void ScanOnce()
    {
        if (scanned) return;
        scanned = true;
        foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                foreach (var attribute in method.GetCustomAttributes<InitializerAttributeBase>(false))
                    attribute.Bind(method);
            }
        }
    }
}

public abstract class InitializerAttribute<T> : InitializerAttributeBase
{
    /// <summary>全初期化メソッド</summary>
    private static MethodInfo[] allInitializers = null;
    private static readonly List<InitializerAttribute<T>> found = new(32);

    public InitializerAttribute() : this(InitializePriority.Normal) { }
    public InitializerAttribute(InitializePriority priority)
    {
        this.priority = priority;
    }

    private readonly InitializePriority priority = InitializePriority.Normal;
    /// <summary>初期化時に呼び出されるメソッド</summary>
    private MethodInfo targetMethod;

    internal override void Bind(MethodInfo method)
    {
        targetMethod = method;
        found.Add(this);
    }

    private static void FindInitializers()
    {
        ScanOnce();
        // 見つかった初期化メソッドをpriority順に並べ替えて配列に変換 (同じ priority の中では見つかった順)
        allInitializers = found.OrderBy(initializer => initializer.priority).Select(initializer => initializer.targetMethod).ToArray();
    }
    public static void InitializeAll()
    {
        // 初回の初期化時に初期化メソッドを探す
        if (allInitializers == null)
        {
            FindInitializers();
        }
        foreach (var initializer in allInitializers)
        {
            initializer.Invoke(null, null);
        }
    }
}

public enum InitializePriority
{
    /// <summary>一番最初に実行される</summary>
    VeryHigh,
    /// <summary>既定値より前に実行される</summary>
    High,
    /// <summary>既定値</summary>
    Normal,
    /// <summary>既定値より後に実行される</summary>
    Low,
    /// <summary>一番最後に実行される</summary>
    VeryLow,
}
