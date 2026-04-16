# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**Dictio** — a WPF desktop application targeting .NET 9 (`net9.0-windows`), namespace `Dictio`. Currently in early scaffolding stage.

## Build & Run

```bash
# Build
dotnet build Dictio/Dictio.csproj

# Run
dotnet run --project Dictio/Dictio.csproj

# Publish (self-contained Windows exe)
dotnet publish Dictio/Dictio.csproj -c Release -r win-x64 --self-contained
```

## Architecture

- `App.xaml` / `App.xaml.cs` — application entry point; `StartupUri` points to `MainWindow.xaml`.
- `MainWindow.xaml` / `MainWindow.xaml.cs` — single main window, code-behind pattern (`partial class`).
- WPF uses XAML for UI layout paired with C# code-behind or MVVM view models. New views should follow the same `*.xaml` + `*.xaml.cs` partial-class pattern, or introduce a `ViewModels/` folder if adopting MVVM.
