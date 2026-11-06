using System.Collections.Generic;
using UnityEngine;

namespace TownOfHostY.Modules.Net;

// ホストが送った SnapTo の本数を数える。公式サーバーは SnapTo を直近約6秒で約330本送るとホストを切断する
// (送る速さには関係なく本数で切られる)。ここでは数えて警告するだけで、送信は止めない。
// 警告が出るなら、その時送っている機能の SnapTo を間引く。
public static class SnapToWatch
{
    const float Window = 6f;
    const int WarnCount = 200;

    static readonly Queue<float> sentAt = new();
    static float lastWarnAt = -Window;

    public static void Record()
    {
        float now = Time.realtimeSinceStartup;
        sentAt.Enqueue(now);
        while (sentAt.Count > 0 && now - sentAt.Peek() > Window) sentAt.Dequeue();

        if (sentAt.Count >= WarnCount && now - lastWarnAt > Window)
        {
            lastWarnAt = now;
            Logger.Warn($"SnapTo が直近{Window}秒で{sentAt.Count}本 (公式サーバーは約330本で切断)", "SnapToWatch");
        }
    }
}
