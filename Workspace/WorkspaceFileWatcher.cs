namespace DotNet.RoslynMcp.Workspace;

public sealed class WorkspaceFileWatcher : IDisposable
{
    public event Action? ReloadRequired;


    public event Action<string>? DocumentChanged;

    private FileSystemWatcher? _watcher;
    public void Start(string solutionPath)
    {
        var directory = Path.GetDirectoryName(solutionPath) ?? throw new InvalidOperationException("Solution directory could not be determined.");

        _watcher?.Dispose();

        _watcher = new FileSystemWatcher(directory)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
        };

        _watcher.Created += OnFileCreated;
        _watcher.Deleted += OnFileDeleted;
        _watcher.Renamed += OnFileRenamed;
        _watcher.Changed += OnFileChanged;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (Path.GetExtension(e.FullPath) == ".cs")
        {
            DocumentChanged?.Invoke(e.FullPath);
            return;
        }

        if (RequiresReload(e.FullPath))
        {
            OnReloadRequired();
        }
    }

    private void OnReloadRequired()
    {
        ReloadRequired?.Invoke();
    }

    private static bool RequiresReload(string path)
    {
        var extension = Path.GetExtension(path);

        if (extension is ".cs" or ".csproj" or ".sln" or ".slnx")
        {
            return true;
        }

        var fileName = Path.GetFileName(path);

        return fileName is "Directory.Build.props" or "Directory.Build.targets" or "Directory.Packages.props" or "global.json";
    }
    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        if (RequiresReload(e.FullPath))
        {
            OnReloadRequired();
        }
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        if (RequiresReload(e.FullPath))
        {
            OnReloadRequired();
        }
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        if (RequiresReload(e.OldFullPath) || RequiresReload(e.FullPath))
        {
            OnReloadRequired();
        }
    }
    public void Dispose()
    {
        _watcher?.Dispose();
    }
}