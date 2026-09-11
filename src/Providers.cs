using System;

internal enum ProviderStatus
{
    Unavailable,
    Stopped,
    Starting,
    Running,
    Waiting,
    Stopping
}

internal interface IProgramProvider
{
    string Key { get; }
    string DisplayName { get; }
    bool IsInstalled { get; }
    bool IsRunning { get; }
    ProviderStatus Status { get; }
    void Start();
    void Stop();
}

internal sealed class ProgramProvider : IProgramProvider
{
    private readonly Func<string> displayName;
    private readonly Func<bool> installed;
    private readonly Func<bool> running;
    private readonly Func<ProviderStatus> status;
    private readonly Action start;
    private readonly Action stop;

    internal ProgramProvider(string key, Func<string> displayName, Func<bool> installed,
        Func<bool> running, Func<ProviderStatus> status, Action start, Action stop)
    {
        Key = key;
        this.displayName = displayName;
        this.installed = installed;
        this.running = running;
        this.status = status;
        this.start = start;
        this.stop = stop;
    }

    public string Key { get; private set; }
    public string DisplayName { get { return displayName(); } }
    public bool IsInstalled { get { return installed(); } }
    public bool IsRunning { get { return running(); } }
    public ProviderStatus Status { get { return status(); } }
    public void Start() { if (IsInstalled) start(); }
    public void Stop() { if (IsRunning) stop(); }
}
