using System;
using System.Collections.Generic;

namespace MFAWPF.Models;

/// <summary>
/// Represents a single task entry and its assigned execution order within a preset.
/// </summary>
public class TaskPresetItem
{
    /// <summary>
    /// Gets or sets the name or identifier of the task.
    /// </summary>
    public string TaskName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the execution sequence order (1-based index).
    /// </summary>
    public int Order { get; set; }
}

/// <summary>
/// Represents a named task execution order preset.
/// </summary>
public class TaskPreset
{
    /// <summary>
    /// Gets or sets the unique name of the preset.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the list of ordered task items in this preset.
    /// </summary>
    public List<TaskPresetItem> Items { get; set; } = new();

    /// <summary>
    /// Gets or sets the timestamp when this preset was created or last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Creates a deep clone of the preset.
    /// </summary>
    public TaskPreset Clone()
    {
        var clone = new TaskPreset
        {
            Name = Name,
            UpdatedAt = UpdatedAt,
            Items = new List<TaskPresetItem>()
        };

        foreach (var item in Items)
        {
            clone.Items.Add(new TaskPresetItem
            {
                TaskName = item.TaskName,
                Order = item.Order
            });
        }

        return clone;
    }
}
