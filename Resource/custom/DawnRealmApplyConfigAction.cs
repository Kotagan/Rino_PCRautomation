using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAWPF.Custom;
using MFAWPF.Extensions.Maa;
using MFAWPF.Helper;

namespace MFAWPF.CustomRuntime;

/// <summary>
/// 注入黎明界配置（策略/公会/难度/次数）。
/// </summary>
public class DawnRealmApplyConfigAction : MaaCustomAction
{
    public override string Name { get; set; } = nameof(DawnRealmApplyConfigAction);

    protected override bool Execute(IMaaContext context, RunArgs args)
    {
        DawnRealmConfig.ApplyParam(DawnRealmConfig.ReadActionParam(args));
        DawnRealmConfig.ResetRunCounter();
        LoggerService.LogInfo(
            $"[DawnRealm] config strategy={DawnRealmConfig.Strategy} guild={DawnRealmConfig.Guild} " +
            $"diff={DawnRealmConfig.Difficulty} maxRuns={DawnRealmConfig.MaxRuns}");
        context.OverrideNext(args.NodeName, ["黎明界进入冒险"]);
        return true;
    }
}
