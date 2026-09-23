namespace BotNC.App.Services;

public sealed class PauseController
{
    private readonly object _gate = new();
    private TaskCompletionSource _resumeSignal = CompletedSignal();

    public bool IsPaused { get; private set; }
    private long _pauseVersion;
    public long PauseVersion => Interlocked.Read(ref _pauseVersion);

    public void Pause()
    {
        lock (_gate)
        {
            if (IsPaused)
            {
                return;
            }

            IsPaused = true;
            Interlocked.Increment(ref _pauseVersion);
            _resumeSignal = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void Resume()
    {
        TaskCompletionSource signal;
        lock (_gate)
        {
            if (!IsPaused)
            {
                return;
            }

            IsPaused = false;
            signal = _resumeSignal;
        }

        signal.TrySetResult();
    }

    public Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return IsPaused
                ? _resumeSignal.Task.WaitAsync(cancellationToken)
                : Task.CompletedTask;
        }
    }

    private static TaskCompletionSource CompletedSignal()
    {
        var signal = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        signal.SetResult();
        return signal;
    }
}
