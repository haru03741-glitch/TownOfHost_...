using System;

namespace TownOfHostL.Roles.Core;

/// <summary>
/// 役職の分類。役職クラスを持つ役職は SimpleRoleInfo.Create の flags で、持たない役職 (属性・CC・HAS など) は
/// RoleCatalog の表で宣言する。判定は CustomRolesHelper の IsXxx から引く。
/// </summary>
[Flags]
public enum RoleFlags : uint
{
    None = 0,
    /// <summary>本編の役職そのもの (Crewmate・Engineer・Impostor など)</summary>
    Vanilla = 1 << 0,
    /// <summary>組で配る役職。人数は組の数で数え、変えられない (FixedCount も兼ねる)</summary>
    Pair = 1 << 1,
    /// <summary>人数の設定を変えられない役職</summary>
    FixedCount = 1 << 2,
    /// <summary>出現率と人数の設定を作らない役職 (ユニットの構成員・封印中の役職など)</summary>
    DontShowOption = 1 << 3,
    /// <summary>Loyalty を付けられない役職</summary>
    DontSelectLoyalty = 1 << 4,
    /// <summary>役職ごとに属性を直接付ける設定を持つ役職 (マッドメイトはこの指定が無くても持つ)</summary>
    AddAddOn = 1 << 5,
    BuffAddOn = 1 << 6,
    DebuffAddOn = 1 << 7,
    /// <summary>バフ・デバフ以外の属性 (LastImpostor・Lovers など)</summary>
    OtherAddOn = 1 << 8,
    /// <summary>役職名の表示で、省略せず先頭に書く属性</summary>
    FirstWriteAddOn = 1 << 9,
    /// <summary>キルボタンをキル以外の効果に使う役職</summary>
    DirectKill = 1 << 10,
    /// <summary>トップに載せる新役職</summary>
    NewRole = 1 << 11,
    CCLeader = 1 << 12,
    CCColorCat = 1 << 13,
    CCNoCat = 1 << 14,
    /// <summary>インポスター陣営に数えるが、他のインポスターの仲間にならない (互いに役職が見えず、キルもできる)</summary>
    LoneImpostor = 1 << 15,
    /// <summary>キルできる第三陣営 (占い・スニッチ・弁護士などが「キルする人」として扱う)</summary>
    NeutralKiller = 1 << 16,
    /// <summary>キルできるクルー (シェリフ系)</summary>
    CrewKiller = 1 << 17,

    // ここから下は役職にも属性にも付けられる性質。判定は Core.PlayerTraits から引く
    /// <summary>追放された人がインポスターかどうかを、自分の名前の所で知る</summary>
    LearnsExiledTeam = 1 << 18,
    /// <summary>死体を通報できない (ReportableByAll の死体は除く)</summary>
    CannotReport = 1 << 19,
    /// <summary>通報できない人でも、この人の死体は通報できる</summary>
    ReportableByAll = 1 << 20,
    /// <summary>死んでもタスクが免除されない</summary>
    KeepsTasksWhenDead = 1 << 21,
    /// <summary>タスクの一覧の頭に「偽のタスク」と出す</summary>
    FakeTasksLabel = 1 << 22,
    /// <summary>会議の始めに、自分の名前を送り直す (タスクの進み具合などを会議の時点の値にする)</summary>
    RefreshesNameOnMeetingStart = 1 << 23,
    /// <summary>タスクを持っていても、クルーの勝利のタスク数に数えない</summary>
    TasksNotCountedForWin = 1 << 24,
}
