using AmongUs.GameOptions;

using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;

namespace TownOfHostL.Roles.Impostor;
public sealed class NormalImpostor : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
         SimpleRoleInfo.Create(
            typeof(NormalImpostor),
            player => new NormalImpostor(player),
            CustomRoles.NormalImpostor,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.ImpDefault + 0,
            null,
            "インポスター"
        );
    public NormalImpostor(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
    }
}