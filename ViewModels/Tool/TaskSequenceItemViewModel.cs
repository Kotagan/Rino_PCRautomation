using System;
using CommunityToolkit.Mvvm.ComponentModel;
using MFAWPF.ViewModels.Tool;

namespace MFAWPF.ViewModels.Tool;

/// <summary>
/// Represents a single task item in the task sequence configuration list.
/// </summary>
public partial class TaskSequenceItemViewModel : ViewModel
{
    private readonly Action<TaskSequenceItemViewModel, int?> _onOrderChangeRequested;

    public TaskSequenceItemViewModel(DragItemViewModel item, Action<TaskSequenceItemViewModel, int?> onOrderChangeRequested)
    {
        DragItem = item;
        _onOrderChangeRequested = onOrderChangeRequested;
        Name = item.Name;
        RawName = item.InterfaceItem?.Name ?? item.Name;
    }

    /// <summary>
    /// Gets the underlying drag item model.
    /// </summary>
    public DragItemViewModel DragItem { get; }

    /// <summary>
    /// Gets the display name of the task.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the raw identifier name of the task.
    /// </summary>
    public string RawName { get; }

    private int? _orderIndex;
    private bool _isInternalUpdating;

    /// <summary>
    /// Gets or sets the execution sequence order number (1-based index, or null if unassigned).
    /// </summary>
    public int? OrderIndex
    {
        get => _orderIndex;
        set
        {
            if (_orderIndex == value)
            {
                OnPropertyChanged(nameof(InputOrderText));
                return;
            }

            if (_isInternalUpdating)
            {
                SetProperty(ref _orderIndex, value);
                OnPropertyChanged(nameof(IsAssigned));
                OnPropertyChanged(nameof(DisplayOrderText));
                OnPropertyChanged(nameof(InputOrderText));
                return;
            }

            _onOrderChangeRequested?.Invoke(this, value);
            OnPropertyChanged(nameof(InputOrderText));
        }
    }

    /// <summary>
    /// Sets order index internally without triggering the user change callback.
    /// </summary>
    public void SetOrderInternal(int? value)
    {
        _isInternalUpdating = true;
        SetProperty(ref _orderIndex, value, nameof(OrderIndex));
        OnPropertyChanged(nameof(IsAssigned));
        OnPropertyChanged(nameof(DisplayOrderText));
        OnPropertyChanged(nameof(InputOrderText));
        _isInternalUpdating = false;
    }

    /// <summary>
    /// Gets or sets the text representation of the order for direct input binding.
    /// </summary>
    public string InputOrderText
    {
        get => _orderIndex.HasValue && _orderIndex.Value > 0 ? _orderIndex.Value.ToString() : string.Empty;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                if (_orderIndex != null)
                {
                    OrderIndex = null;
                }
                OnPropertyChanged(nameof(InputOrderText));
                return;
            }

            if (int.TryParse(value.Trim(), out var parsedInt))
            {
                OrderIndex = parsedInt;
            }
            else
            {
                OnPropertyChanged(nameof(InputOrderText));
            }
        }
    }

    /// <summary>
    /// Gets whether this task is currently assigned a sequence number.
    /// </summary>
    public bool IsAssigned => OrderIndex.HasValue && OrderIndex.Value > 0;

    /// <summary>
    /// Gets the string representation of the order for UI display.
    /// </summary>
    public string DisplayOrderText => OrderIndex.HasValue && OrderIndex.Value > 0 ? OrderIndex.Value.ToString() : "-";
}
