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


# Safety Vision Monitor

제조 현장 CCTV 안전 모니터링 시스템

## 기술 스택
- WPF (.NET 8.0)
- Syncfusion UI Components 30.1.40
- OpenCVSharp4
- Entity Framework Core (SQLite)
- MVVM Pattern (CommunityToolkit.Mvvm)

## 주요 기능
- 실시간 다중 카메라 모니터링
- AI 기반 안전 이벤트 감지 (YOLO)
- 3D 위험 구역 설정
- 사람 추적 (Multi-camera tracking)
- 이벤트 로깅 및 통계

## 프로젝트 구조
SafetyVisionMonitor/
  ├── .idea/                          # JetBrains Rider IDE 설정
  ├── .vs/                           # Visual Studio IDE 설정
  ├── SafetyVisionMonitor/           # 메인 프로젝트
  │   ├── Converters/               # XAML 데이터 변환기
  │   │   ├── BoolToColorConverter.cs
  │   │   ├── BoolToYesNoConverter.cs
  │   │   ├── ColorToBrushConverter.cs
  │   │   ├── CountToBoolConverter.cs
  │   │   ├── EditModeToTitleConverter.cs
  │   │   ├── EnumBooleanConverter.cs
  │   │   ├── NullToVisibilityConverter.cs
  │   │   ├── TemperatureToColorConverter.cs
  │   │   └── UsageToColorConverter.cs
  │   ├── Database/                 # 데이터베이스 관련
  │   │   └── AppDbContext.cs
  │   ├── Helpers/                  # 헬퍼 클래스
  │   │   └── ImageConverter.cs
  │   ├── Models/                   # 데이터 모델
  │   │   ├── AIModel.cs
  │   │   ├── Camera.cs
  │   │   ├── SafetyEvent.cs
  │   │   └── Zone3D.cs
  │   ├── Resources/                # 리소스 파일
  │   │   └── Styles/
  │   │       └── CustomStyles.xaml
  │   ├── Services/                 # 서비스 클래스
  │   │   ├── ApplicationData.cs
  │   │   ├── CameraService.cs
  │   │   ├── DatabaseService.cs
  │   │   └── MonitoringService.cs
  │   ├── ViewModels/              # MVVM ViewModel
  │   │   ├── AIModelViewModel.cs
  │   │   ├── BaseViewModel.cs
  │   │   ├── CameraConfigDialogViewModel.cs
  │   │   ├── CameraManageViewModel.cs
  │   │   ├── DashboardViewModel.cs
  │   │   ├── EventLogViewModel.cs
  │   │   ├── HistoryViewModel.cs
  │   │   ├── MainViewModel.cs
  │   │   ├── TrackingSetupViewModel.cs
  │   │   └── ZoneSetupViewModel.cs
  │   ├── Views/                   # XAML 뷰
  │   │   ├── AIModelView.xaml/.cs
  │   │   ├── CameraConfigDialog.xaml/.cs
  │   │   ├── CameraManageView.xaml/.cs
  │   │   ├── DashboardView.xaml/.cs
  │   │   ├── EventLogView.xaml/.cs
  │   │   ├── HistoryView.xaml/.cs
  │   │   ├── SplashWindow.xaml/.cs
  │   │   ├── TrackingSetupView.xaml/.cs
  │   │   └── ZoneSetupView.xaml/.cs
  │   ├── App.xaml/.cs             # 애플리케이션 진입점
  │   ├── MainWindow.xaml/.cs      # 메인 윈도우
  │   ├── AssemblyInfo.cs          # 어셈블리 정보
  │   ├── SafetyVisionMonitor.csproj # 프로젝트 파일
  │   └── appsettings.json         # 설정 파일
  ├── SafetyVisionMonitor.sln      # 솔루션 파일
  └── CLAUDE.md                    # 프로젝트 문서


## 설치 방법
1. Visual Studio 2022 설치
2. .NET 8.0 SDK 설치
3. 프로젝트 클론
4. NuGet 패키지 복원
5. 실행

## 현재 개발 상황
- [x] 기본 UI 구조
- [x] 카메라 연결
- [ ] YOLO 모델 통합
- [ ] 3D 구역 그리기
- [ ] 실시간 알림