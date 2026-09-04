namespace MFAWPF.Services.Characters;

internal enum CharacterTaskState
{
    QueueStarted,
    TaskStarted,
    TaskSucceeded,
    TaskFailed,
    QueueSucceeded,
    QueueStopped,
}

internal sealed class CharacterTaskStateChangedEventArgs(
    CharacterTaskState state,
    string taskKey = "",
    string taskName = "") : EventArgs
{
    public CharacterTaskState State { get; } = state;

    public string TaskKey { get; } = taskKey;

    public string TaskName { get; } = taskName;
}

internal sealed class CharacterTaskStateService
{
    public CharacterTaskStateService()
    {
    }

    public event EventHandler<CharacterTaskStateChangedEventArgs>? StateChanged;

    public CharacterTaskState CurrentState { get; private set; } = CharacterTaskState.QueueStopped;

    public void Notify(CharacterTaskState state, string taskKey = "", string taskName = "")
    {
        CurrentState = state;
        StateChanged?.Invoke(this, new CharacterTaskStateChangedEventArgs(state, taskKey, taskName));
    }
}
