using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Concurrent;

namespace DotNet.RoslynMcp.Workspace;

public sealed class WorkspaceManager : IAsyncDisposable
{
    private MSBuildWorkspace? _workspace;
    private readonly SemaphoreSlim _workspaceGate = new(1, 1);

    private string? _solutionPath;
    private Solution? _solution;

    private int _reloadRequired;
    private readonly WorkspaceFileWatcher _fileWatcher = new();
    private readonly ConcurrentDictionary<string, byte> _changedDocuments = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DocumentId> _documentIds = new(StringComparer.OrdinalIgnoreCase);

    public WorkspaceManager()
    {
        _fileWatcher.ReloadRequired += RequestReload;
        _fileWatcher.DocumentChanged += RequestDocumentRefresh;
    }
    public async Task<Solution> LoadSolutionAsync(string solutionPath,  CancellationToken cT = default)
    {
        await _workspaceGate.WaitAsync(cT);

        try
        {
            _workspace = MSBuildWorkspace.Create();
            _solution = await _workspace.OpenSolutionAsync(solutionPath, cancellationToken: cT);

            _solutionPath = solutionPath;
            BuildDocumentIndex();
            _fileWatcher.Start(solutionPath);

            return _solution;
        }
        finally
        {
            _workspaceGate.Release();
        }
    }

    public async Task<Solution> GetSolutionAsync(CancellationToken cancellationToken = default)
    {
        await _workspaceGate.WaitAsync(cancellationToken);

        try
        {
            if (_solution is null || Volatile.Read(ref _reloadRequired) == 1)
            {
                await ReloadSolutionAsync(cancellationToken);
                Interlocked.Exchange(ref _reloadRequired, 0);
            }
            else
            {
                await RefreshChangedDocumentsAsync(cancellationToken);
            }

            return _solution;
        }
        finally
        {
            _workspaceGate.Release();
        }
    }
    private void RequestDocumentRefresh(string path)
    {
        _changedDocuments.TryAdd(path, 0);
    }
    public void RequestReload()
    {
        Interlocked.Exchange(ref _reloadRequired, 1);
    }
    private async Task RefreshChangedDocumentsAsync(CancellationToken cancellationToken)
    {
        foreach (var filePath in _changedDocuments.Keys)
        {
            try
            {
                await RefreshDocumentAsync(filePath, cancellationToken);
                _changedDocuments.TryRemove(filePath, out _);
            }
            catch
            {
                RequestReload();
                throw;
            }
        }
    }
    private async Task RefreshDocumentAsync(string filePath, CancellationToken cancellationToken)
    {
        if (_solution is null)
        {
            return;
        }

        if (!_documentIds.TryGetValue(filePath, out var documentId))
        {
            RequestReload();
            return;
        }

        var text = await File.ReadAllTextAsync(filePath, cancellationToken);
        _solution = _solution.WithDocumentText(documentId, SourceText.From(text));
    }
    private async Task ReloadSolutionAsync(CancellationToken cancellationToken)
    {
        if (_solutionPath is null)
        {
            throw new InvalidOperationException("Solution has not been loaded.");
        }

        _solution = null;

        _workspace?.Dispose();
        _workspace = MSBuildWorkspace.Create();

        _solution = await _workspace.OpenSolutionAsync(_solutionPath, cancellationToken: cancellationToken);
        BuildDocumentIndex();
    }
    private void BuildDocumentIndex()
    {
        _documentIds.Clear();

        if (_solution is null)
        {
            return;
        }

        foreach (var document in _solution.Projects.SelectMany(project => project.Documents))
        {
            if (document.FilePath is not null)
            {
                _documentIds[document.FilePath] = document.Id;
            }
        }
    }
    public ValueTask DisposeAsync()
    {
        _fileWatcher.ReloadRequired -= RequestReload;
        _fileWatcher.DocumentChanged -= RequestDocumentRefresh;
        _fileWatcher.Dispose();
        _workspace?.Dispose();
        _workspaceGate.Dispose();
        return ValueTask.CompletedTask;
    }
}