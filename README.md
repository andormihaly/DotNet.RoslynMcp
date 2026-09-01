# DotNet.RoslynMcp

A read-only Model Context Protocol (MCP) server that provides
Roslyn-based semantic code intelligence for .NET solutions.

DotNet.RoslynMcp allows AI coding assistants and MCP clients to
understand .NET codebases using Roslyn instead of relying only on
text-based search.

## Features

-   Symbol search and resolution
-   References, implementations, callers and overrides
-   Type hierarchy and call graph analysis
-   Semantic code search
-   File and project inspection
-   Project dependency analysis
-   ASP.NET Core endpoint discovery
-   Dependency injection registration discovery
-   Automatic workspace refresh when source files change
-   14 read-only MCP tools

## Requirements

-   .NET 10 SDK
-   A `.sln` or `.slnx` solution
-   An MCP-compatible client

## Installation

``` bash
dotnet tool install --global DotNet.RoslynMcp
```

## Usage

DotNet.RoslynMcp requires the full path to the solution:

``` bash
dotnet-roslyn-mcp --solution "C:\Work\MyProject\MyProject.slnx"
```

The server communicates with MCP clients over `stdio`.

## Documentation

See [docs/Documentation.md](docs/Documentation.md) for the complete tool
reference, architecture, workspace behavior, local installation and MCP
Inspector usage.

## License

See the repository license for details.
