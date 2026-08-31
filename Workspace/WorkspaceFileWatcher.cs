using System.Collections.Concurrent;

namespace DotNet.RoslynMcp.Workspace;

public sealed class WorkspaceFileWatcher : IDisposable
{
    public event Action? ReloadRequired;


    public event Action<string>? DocumentChanged;

    private FileSystemWatcher? _watcher;
    private readonly TimeSpan _debounceDelay = TimeSpan.FromMilliseconds(250);
    private Timer? _debounceTimer;
    private int _processingChanges;
    private readonly ConcurrentDictionary<string, WatcherChangeTypes> _pendingChanges = new(StringComparer.OrdinalIgnoreCase);

    public void Start(string solutionPath)
    {
        _watcher?.Dispose();
        _pendingChanges.Clear();

        var solutionDirectory = Path.GetDirectoryName(solutionPath)  ?? throw new InvalidOperationException("Solution directory could not be determined.");

        _watcher = new FileSystemWatcher(solutionDirectory)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
        };

        _watcher.Created += OnFileCreated;
        _watcher.Deleted += OnFileDeleted;
        _watcher.Renamed += OnFileRenamed;
        _watcher.Changed += OnFileChanged;
        _watcher.Error += OnWatcherError;

        _debounceTimer ??= new Timer(ProcessPendingChanges);

        _watcher.EnableRaisingEvents = true;
    }
    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        OnReloadRequired();
    }
    private void ProcessPendingChanges(object? state)
    {
        if (Interlocked.Exchange(ref _processingChanges, 1) == 1)
        {
            _debounceTimer?.Change(_debounceDelay, Timeout.InfiniteTimeSpan);
            return;
        }

        try
        {
            var changes = new Dictionary<string, WatcherChangeTypes>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in _pendingChanges.Keys)
            {
                if (_pendingChanges.TryRemove(path, out var changeType))
                {
                    changes[path] = changeType;
                }
            }

            foreach (var (path, changeType) in changes)
            {
                if ((changeType & (WatcherChangeTypes.Created | WatcherChangeTypes.Deleted | WatcherChangeTypes.Renamed)) != 0 && RequiresReload(path))
                {
                    OnReloadRequired();
                    return;
                }

                if ((changeType & WatcherChangeTypes.Changed) != 0 && PathRequiresReloadOnChange(path))
                {
                    OnReloadRequired();
                    return;
                }
            }

            foreach (var (path, changeType) in changes)
            {
                if ((changeType & WatcherChangeTypes.Changed) != 0 && string.Equals(Path.GetExtension(path), ".cs", StringComparison.OrdinalIgnoreCase))
                {
                    DocumentChanged?.Invoke(path);
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _processingChanges, 0);
        }
    }
    private static bool PathRequiresReloadOnChange(string path)
    {
        return RequiresReload(path) && !string.Equals(Path.GetExtension(path), ".cs", StringComparison.OrdinalIgnoreCase);
    }
    private void QueueChange(string path, WatcherChangeTypes changeType)
    {
        if (IsIgnoredPath(path))
        {
            return;
        }

        _pendingChanges.AddOrUpdate(path, changeType, (_, existing) => existing | changeType);
        _debounceTimer?.Change(_debounceDelay, Timeout.InfiniteTimeSpan);
    }

    private static bool IsIgnoredPath(string path)
    {
        var directory = Path.GetDirectoryName(path);

        while (directory is not null)
        {
            var directoryName = Path.GetFileName(directory);

            if (string.Equals(directoryName, "bin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(directoryName, "obj", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return false;
    }

    private void OnReloadRequired()
    {
        ReloadRequired?.Invoke();
    }

    private static bool RequiresReload(string path)
    {
        var extension = Path.GetExtension(path);

        if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) || extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) || extension.Equals(".sln", StringComparison.OrdinalIgnoreCase) || extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var fileName = Path.GetFileName(path);

        return fileName.Equals("Directory.Build.props", StringComparison.OrdinalIgnoreCase) || fileName.Equals("Directory.Build.targets", StringComparison.OrdinalIgnoreCase) || fileName.Equals("Directory.Packages.props", StringComparison.OrdinalIgnoreCase) || fileName.Equals("global.json", StringComparison.OrdinalIgnoreCase);
    }
    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        QueueChange(e.FullPath, WatcherChangeTypes.Created);
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        QueueChange(e.FullPath, WatcherChangeTypes.Deleted);
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        QueueChange(e.FullPath, WatcherChangeTypes.Changed);
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        QueueChange(e.OldFullPath, WatcherChangeTypes.Renamed);
        QueueChange(e.FullPath, WatcherChangeTypes.Renamed);
    }
    public void Dispose()
    {
        _debounceTimer?.Dispose();
        _watcher?.Dispose();
    }
}