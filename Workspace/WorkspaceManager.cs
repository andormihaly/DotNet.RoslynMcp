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
    private readonly Dictionary<ProjectId, Compilation> _compilations = [];

    public WorkspaceManager()
    {
        _fileWatcher.ReloadRequired += RequestReload;
        _fileWatcher.DocumentChanged += RequestDocumentRefresh;
    }
    public async Task<Solution> LoadSolutionAsync(string solutionPath, CancellationToken cT = default)
    {
        await _workspaceGate.WaitAsync(cT);

        try
        {
            _workspace?.Dispose();
            _workspace = null;
            _solution = null;

            _workspace = MSBuildWorkspace.Create();
            _solution = await _workspace.OpenSolutionAsync(solutionPath, cancellationToken: cT);

            _solutionPath = solutionPath;
            BuildDocumentIndex();
            await BuildCompilationCacheAsync(cT);
            _fileWatcher.Start(solutionPath);

            return _solution;
        }
        finally
        {
            _workspaceGate.Release();
        }
    }

    public Compilation GetCompilation(ProjectId projectId)
    {
        if (!_compilations.TryGetValue(projectId, out var compilation))
        {
            throw new InvalidOperationException($"Compilation for project '{projectId}' is not available.");
        }

        return compilation;
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

                if (Volatile.Read(ref _reloadRequired) == 1)
                {
                    await ReloadSolutionAsync(cancellationToken);
                    Interlocked.Exchange(ref _reloadRequired, 0);
                }
            }

            return _solution ?? throw new InvalidOperationException("Solution could not be loaded.");
        }
        finally
        {
            _workspaceGate.Release();
        }
    }
    private async Task BuildCompilationCacheAsync(CancellationToken cancellationToken)
    {
        if (_solution is null)
        {
            return;
        }

        _compilations.Clear();

        foreach (var project in _solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(cancellationToken);

            if (compilation is not null)
            {
                _compilations[project.Id] = compilation;
            }
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

        var document = _solution.GetDocument(documentId);

        if (document is null)
        {
            RequestReload();
            return;
        }

        var text = await File.ReadAllTextAsync(filePath, cancellationToken);

        _solution = _solution.WithDocumentText(documentId, SourceText.From(text));
        _compilations.Remove(document.Project.Id);

        var updatedProject = _solution.GetProject(document.Project.Id);

        if (updatedProject is not null)
        {
            var compilation = await updatedProject.GetCompilationAsync(cancellationToken);

            if (compilation is not null)
            {
                _compilations[updatedProject.Id] = compilation;
            }
        }
    }
    private async Task ReloadSolutionAsync(CancellationToken cT)
    {
        if (_solutionPath is null)
        {
            throw new InvalidOperationException("Solution has not been loaded.");
        }


        _changedDocuments.Clear();
        _solution = null;
        _compilations.Clear();

        _workspace?.Dispose();
        _workspace = MSBuildWorkspace.Create();

        _solution = await _workspace.OpenSolutionAsync(_solutionPath, cancellationToken: cT);
        BuildDocumentIndex();
        await BuildCompilationCacheAsync(cT);
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