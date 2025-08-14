using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SafetyVisionMonitor.Models;
using SafetyVisionMonitor.Shared.ViewModels.Base;
using Microsoft.Win32;

namespace SafetyVisionMonitor.ViewModels
{
    public partial class SafetySettingsViewModel : BaseViewModel
    {
        [ObservableProperty]
        private SafetySettings settings;

        private readonly string settingsFilePath;

        public SafetySettingsViewModel()
        {
            Title = "안전 기능 설정";
            StatusMessage = "설정을 변경하세요.";
            
            // 설정 파일 경로 설정 (appsettings.json과 별도 파일로 관리)
            settingsFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SafetyVisionMonitor",
                "safety-settings.json"
            );

            // 기본 설정으로 초기화
            Settings = new SafetySettings();
            
            // 설정 로드
            _ = LoadSettingsAsync();
        }

        #region 설정 관리 명령

        [RelayCommand]
        private async Task LoadSettings()
        {
            await LoadSettingsAsync();
        }

        [RelayCommand]
        private async Task SaveSettings()
        {
            await SaveSettingsAsync();
        }

        [RelayCommand]
        private void ResetToDefaults()
        {
            var result = MessageBox.Show(
                "모든 설정을 기본값으로 되돌리시겠습니까?\n\n현재 설정이 모두 손실됩니다.",
                "설정 초기화",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (result == MessageBoxResult.Yes)
            {
                Settings.ResetToDefaults();
                OnPropertyChanged(nameof(Settings));
                StatusMessage = "설정이 기본값으로 초기화되었습니다.";
            }
        }

        [RelayCommand]
        private async Task ExportSettings()
        {
            try
            {
                var saveFileDialog = new SaveFileDialog
                {
                    Title = "설정 파일 내보내기",
                    Filter = "JSON 파일 (*.json)|*.json|모든 파일 (*.*)|*.*",
                    DefaultExt = "json",
                    FileName = $"safety-settings-{DateTime.Now:yyyyMMdd-HHmmss}.json"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    await SaveSettingsToFileAsync(saveFileDialog.FileName);
                    StatusMessage = $"설정이 {saveFileDialog.FileName} 파일로 내보내졌습니다.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"설정 내보내기 실패: {ex.Message}";
                MessageBox.Show($"설정 내보내기 중 오류가 발생했습니다:\n{ex.Message}", "오류", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private async Task ImportSettings()
        {
            try
            {
                var openFileDialog = new OpenFileDialog
                {
                    Title = "설정 파일 가져오기",
                    Filter = "JSON 파일 (*.json)|*.json|모든 파일 (*.*)|*.*",
                    DefaultExt = "json"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    await LoadSettingsFromFileAsync(openFileDialog.FileName);
                    StatusMessage = $"설정이 {openFileDialog.FileName} 파일에서 가져와졌습니다.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"설정 가져오기 실패: {ex.Message}";
                MessageBox.Show($"설정 가져오기 중 오류가 발생했습니다:\n{ex.Message}", "오류", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region 외부 파일 선택 명령

        [RelayCommand]
        private void SelectSoundFile()
        {
            try
            {
                var openFileDialog = new OpenFileDialog
                {
                    Title = "외부 알림 사운드 파일 선택",
                    Filter = "오디오 파일 (*.wav;*.mp3;*.wma)|*.wav;*.mp3;*.wma|모든 파일 (*.*)|*.*",
                    DefaultExt = "wav"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    Settings.ExternalSoundFilePath = openFileDialog.FileName;
                    OnPropertyChanged(nameof(Settings));
                    StatusMessage = $"사운드 파일이 선택되었습니다: {Path.GetFileName(openFileDialog.FileName)}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"사운드 파일 선택 실패: {ex.Message}";
            }
        }

        [RelayCommand]
        private void TestSoundFile()
        {
            if (string.IsNullOrEmpty(Settings.ExternalSoundFilePath) || !File.Exists(Settings.ExternalSoundFilePath))
            {
                StatusMessage = "사운드 파일이 선택되지 않았거나 파일을 찾을 수 없습니다.";
                return;
            }

            try
            {
                // TODO: 실제 사운드 재생 구현 필요
                StatusMessage = $"사운드 테스트: {Path.GetFileName(Settings.ExternalSoundFilePath)} (음량: {Settings.AlertVolume}%)";
                MessageBox.Show($"사운드 파일 테스트:\n{Settings.ExternalSoundFilePath}\n음량: {Settings.AlertVolume}%", 
                    "사운드 테스트", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusMessage = $"사운드 테스트 실패: {ex.Message}";
            }
        }

        #endregion

        #region 설정 검증 명령

        [RelayCommand]
        private void ValidateSettings()
        {
            if (Settings.ValidateSettings(out string errorMessage))
            {
                StatusMessage = "모든 설정이 유효합니다.";
                MessageBox.Show("모든 설정이 유효합니다.", "설정 검증", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                StatusMessage = $"설정 오류: {errorMessage}";
                MessageBox.Show($"설정 오류:\n{errorMessage}", "설정 검증", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        #endregion

        #region Private Methods

        private async Task LoadSettingsAsync()
        {
            IsLoading = true;
            try
            {
                if (File.Exists(settingsFilePath))
                {
                    await LoadSettingsFromFileAsync(settingsFilePath);
                    StatusMessage = "설정이 로드되었습니다.";
                }
                else
                {
                    StatusMessage = "설정 파일이 없어 기본값을 사용합니다.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"설정 로드 실패: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Load settings error: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task SaveSettingsAsync()
        {
            IsLoading = true;
            try
            {
                // 설정 유효성 검사
                if (!Settings.ValidateSettings(out string errorMessage))
                {
                    StatusMessage = $"설정 오류: {errorMessage}";
                    MessageBox.Show($"설정을 저장할 수 없습니다:\n{errorMessage}", "설정 오류", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                await SaveSettingsToFileAsync(settingsFilePath);
                StatusMessage = "설정이 저장되었습니다.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"설정 저장 실패: {ex.Message}";
                MessageBox.Show($"설정 저장 중 오류가 발생했습니다:\n{ex.Message}", "오류", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadSettingsFromFileAsync(string filePath)
        {
            var json = await File.ReadAllTextAsync(filePath);
            var loadedSettings = JsonSerializer.Deserialize<SafetySettings>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            });

            if (loadedSettings != null)
            {
                Settings = loadedSettings;
                OnPropertyChanged(nameof(Settings));
            }
        }

        private async Task SaveSettingsToFileAsync(string filePath)
        {
            // 디렉토리가 없으면 생성
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await File.WriteAllTextAsync(filePath, json);
        }

        #endregion

        #region 기능별 설정 그룹 속성 (UI 바인딩용)

        /// <summary>
        /// 안전보호구 감시 기능이 하나라도 활성화되어 있는지
        /// </summary>
        public bool HasSafetyEquipmentMonitoring => 
            Settings.IsHelmetMonitoringEnabled || 
            Settings.IsGlovesMonitoringEnabled || 
            Settings.IsSafetyBootsMonitoringEnabled;

        /// <summary>
        /// 개인정보 보호 기능이 하나라도 활성화되어 있는지
        /// </summary>
        public bool HasPrivacyProtection => 
            Settings.IsFaceBlurEnabled || 
            Settings.IsFullBodyBlurEnabled;

        /// <summary>
        /// 구역 감시 기능이 하나라도 활성화되어 있는지
        /// </summary>
        public bool HasZoneMonitoring => 
            Settings.IsDangerZoneMonitoringEnabled || 
            Settings.IsWarningZoneMonitoringEnabled ||
            Settings.IsSafetyMonitoringZoneEnabled;

        /// <summary>
        /// 불안전 행동 감시 기능이 하나라도 활성화되어 있는지
        /// </summary>
        public bool HasUnsafeBehaviorMonitoring => 
            Settings.IsTwoPersonWorkMonitoringEnabled || 
            Settings.IsFallDetectionEnabled || 
            Settings.IsEmergencySignalDetectionEnabled;

        /// <summary>
        /// 외부 연동 기능이 하나라도 활성화되어 있는지
        /// </summary>
        public bool HasExternalIntegration => 
            Settings.IsExternalSoundAlertEnabled || 
            Settings.IsMqttIntegrationEnabled || 
            Settings.IsMitsubishiPlcIntegrationEnabled || 
            Settings.IsSafetyPlcIntegrationEnabled || 
            Settings.IsCcLinkIntegrationEnabled;

        #endregion

        /// <summary>
        /// 설정 변경 시 관련 속성들 업데이트
        /// </summary>
        partial void OnSettingsChanged(SafetySettings value)
        {
            OnPropertyChanged(nameof(HasSafetyEquipmentMonitoring));
            OnPropertyChanged(nameof(HasPrivacyProtection));
            OnPropertyChanged(nameof(HasZoneMonitoring));
            OnPropertyChanged(nameof(HasUnsafeBehaviorMonitoring));
            OnPropertyChanged(nameof(HasExternalIntegration));
        }
    }
}