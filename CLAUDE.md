# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

SafetyVisionMonitor is a WPF application built with .NET 8.0 targeting Windows. It uses a modern WPF stack with MVVM pattern support through CommunityToolkit.Mvvm and Syncfusion UI components for enhanced user interface elements.

## Architecture

- **Framework**: .NET 8.0 Windows WPF application
- **MVVM Pattern**: Uses CommunityToolkit.Mvvm for MVVM implementation
- **UI Components**: Syncfusion controls including SfGrid and SfNavigationDrawer
- **Theme**: Syncfusion FluentDark theme for modern dark UI
- **Configuration**: Microsoft.Extensions.Configuration.Json for JSON-based configuration

## Key Dependencies

- **CommunityToolkit.Mvvm**: MVVM framework for databinding and commands
- **Syncfusion.SfGrid.WPF**: Advanced data grid component
- **Syncfusion.SfNavigationDrawer.WPF**: Navigation drawer control
- **Syncfusion.Themes.FluentDark.WPF**: Dark theme styling
- **Microsoft.Extensions.Configuration.Json**: Configuration management

## Development Commands

### Build
```bash
dotnet build SafetyVisionMonitor.sln
```

### Run Application
```bash
dotnet run --project SafetyVisionMonitor/SafetyVisionMonitor.csproj
```

### Clean Solution
```bash
dotnet clean SafetyVisionMonitor.sln
```

### Restore Packages
```bash
dotnet restore SafetyVisionMonitor.sln
```

## Project Structure

- `SafetyVisionMonitor.sln` - Visual Studio solution file
- `SafetyVisionMonitor/` - Main application project
  - `App.xaml/.cs` - Application entry point and resources
  - `MainWindow.xaml/.cs` - Main window UI and code-behind
  - `SafetyVisionMonitor.csproj` - Project file with dependencies

## Development Notes

- The project uses implicit usings and nullable reference types enabled
- XAML files use standard WPF namespaces with Syncfusion controls integration
- Code-behind follows standard WPF patterns with partial classes
- The application is configured for Windows-only deployment (WinExe output type)