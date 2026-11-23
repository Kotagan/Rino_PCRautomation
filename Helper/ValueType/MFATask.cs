using CommunityToolkit.Mvvm.ComponentModel;
using MaaFramework.Binding;
using MFAWPF.Extensions.Maa;
using MFAWPF.Helper;
using MFAWPF.Views.UI;

namespace MFAWPF.Helper.ValueType;

public partial class MFATask : ObservableObject
{
    public enum MFATaskType
    {
        MFA,
        MAAFW
    }

    [ObservableProperty] private string? _name = string.Empty;
    [ObservableProperty] private MFATaskType _type = MFATaskType.MFA;
    [ObservableProperty] private int _count = 1;
    [ObservableProperty] private Func<Task> _action;
    [ObservableProperty] private Dictionary<string, TaskModel> _tasks = new();

    /// <summary>
    /// Gets or sets the stable identifier used for top-level task integrations.
    /// </summary>
    public string? Key { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this task reports desktop-character state.
    /// </summary>
    public bool ReportsCharacterState { get; set; }


    public async Task<bool> Run(CancellationToken token)
    {
        try
        {
            for (int i = 0; i < Count; i++)
            {
                token.ThrowIfCancellationRequested();
                if (Type == MFATaskType.MAAFW)
                    RootView.AddLogByKey("TaskStart", null, true, Name ?? string.Empty);
                await Action();
            }

            if (Type == MFATaskType.MAAFW)
                RootView.AddLogByKey("TaskRunSucceeded", BrushConverterHelper.ConvertToBrush("ForestGreen"), true, Name ?? string.Empty);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            LoggerService.LogError($"任务异常 [{Name}]: {ex}");
            if (Type == MFATaskType.MAAFW)
                RootView.AddLogByKey("TaskRunFailed", BrushConverterHelper.ConvertToBrush("OrangeRed"), false, Name ?? string.Empty, ex.Message);
            else
                RootView.AddLog($"err:{Name}: {ex.Message}", BrushConverterHelper.ConvertToBrush("OrangeRed"));
            return false;
        }
    }
}
