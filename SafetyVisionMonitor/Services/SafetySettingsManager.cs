using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using SafetyVisionMonitor.Models;

namespace SafetyVisionMonitor.Services
{
    /// <summary>
    /// 안전 설정을 전역적으로 관리하는 서비스
    /// </summary>
    public class SafetySettingsManager
    {
        private static SafetySettingsManager? _instance;
        private static readonly object _lock = new object();
        
        private SafetySettings _currentSettings;
        private readonly string _settingsFilePath;
        
        // 설정 변경 이벤트
        public event EventHandler<SafetySettings>? SettingsChanged;
        
        private SafetySettingsManager()
        {
            _settingsFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SafetyVisionMonitor",
                "safety-settings.json"
            );
            
            _currentSettings = new SafetySettings();
            _ = LoadSettingsAsync();
        }
        
        public static SafetySettingsManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                            _instance = new SafetySettingsManager();
                    }
                }
                return _instance;
            }
        }
        
        /// <summary>
        /// 현재 설정 반환
        /// </summary>
        public SafetySettings CurrentSettings => _currentSettings;
        
        /// <summary>
        /// 설정 업데이트
        /// </summary>
        public async Task UpdateSettingsAsync(SafetySettings newSettings)
        {
            var oldSettings = _currentSettings;
            _currentSettings = newSettings;
            
            // 파일에 저장
            await SaveSettingsAsync();
            
            // 변경 이벤트 발생
            SettingsChanged?.Invoke(this, _currentSettings);
            
            // 개별 설정 변경 로깅
            LogSettingsChanges(oldSettings, newSettings);
        }
        
        /// <summary>
        /// 설정 로드
        /// </summary>
        private async Task LoadSettingsAsync()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = await File.ReadAllTextAsync(_settingsFilePath);
                    var settings = JsonSerializer.Deserialize<SafetySettings>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    
                    if (settings != null)
                    {
                        _currentSettings = settings;
                        System.Diagnostics.Debug.WriteLine("안전 설정 로드 완료");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("설정 파일 없음 - 기본값 사용");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"설정 로드 실패: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 설정 저장
        /// </summary>
        private async Task SaveSettingsAsync()
        {
            try
            {
                var directory = Path.GetDirectoryName(_settingsFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                var json = JsonSerializer.Serialize(_currentSettings, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                
                await File.WriteAllTextAsync(_settingsFilePath, json);
                System.Diagnostics.Debug.WriteLine("안전 설정 저장 완료");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"설정 저장 실패: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 설정 변경 사항 로깅
        /// </summary>
        private void LogSettingsChanges(SafetySettings oldSettings, SafetySettings newSettings)
        {
            // 개인정보 보호 설정 변경
            if (oldSettings.IsFaceBlurEnabled != newSettings.IsFaceBlurEnabled)
            {
                System.Diagnostics.Debug.WriteLine($"얼굴 흐림 설정 변경: {oldSettings.IsFaceBlurEnabled} -> {newSettings.IsFaceBlurEnabled}");
            }
            
            if (oldSettings.IsFullBodyBlurEnabled != newSettings.IsFullBodyBlurEnabled)
            {
                System.Diagnostics.Debug.WriteLine($"몸 전체 흐림 설정 변경: {oldSettings.IsFullBodyBlurEnabled} -> {newSettings.IsFullBodyBlurEnabled}");
            }
            
            // 안전보호구 감시 설정 변경
            if (oldSettings.IsHelmetMonitoringEnabled != newSettings.IsHelmetMonitoringEnabled)
            {
                System.Diagnostics.Debug.WriteLine($"안전모 감시 설정 변경: {oldSettings.IsHelmetMonitoringEnabled} -> {newSettings.IsHelmetMonitoringEnabled}");
            }
            
            if (oldSettings.IsGlovesMonitoringEnabled != newSettings.IsGlovesMonitoringEnabled)
            {
                System.Diagnostics.Debug.WriteLine($"안전장갑 감시 설정 변경: {oldSettings.IsGlovesMonitoringEnabled} -> {newSettings.IsGlovesMonitoringEnabled}");
            }
            
            if (oldSettings.IsSafetyBootsMonitoringEnabled != newSettings.IsSafetyBootsMonitoringEnabled)
            {
                System.Diagnostics.Debug.WriteLine($"안전화 감시 설정 변경: {oldSettings.IsSafetyBootsMonitoringEnabled} -> {newSettings.IsSafetyBootsMonitoringEnabled}");
            }
        }
        
        /// <summary>
        /// 특정 기능이 활성화되어 있는지 확인
        /// </summary>
        public bool IsFeatureEnabled(string featureName)
        {
            return featureName.ToLower() switch
            {
                "faceblur" => _currentSettings.IsFaceBlurEnabled,
                "bodyblur" => _currentSettings.IsFullBodyBlurEnabled,
                "helmet" => _currentSettings.IsHelmetMonitoringEnabled,
                "gloves" => _currentSettings.IsGlovesMonitoringEnabled,
                "boots" => _currentSettings.IsSafetyBootsMonitoringEnabled,
                "dangerzone" => _currentSettings.IsDangerZoneMonitoringEnabled,
                "warningzone" => _currentSettings.IsWarningZoneMonitoringEnabled,
                "falldetection" => _currentSettings.IsFallDetectionEnabled,
                "emergencysignal" => _currentSettings.IsEmergencySignalDetectionEnabled,
                "twoPersonwork" => _currentSettings.IsTwoPersonWorkMonitoringEnabled,
                "workerdensity" => _currentSettings.IsWorkerDensityMonitoringEnabled,
                "externalsound" => _currentSettings.IsExternalSoundAlertEnabled,
                _ => false
            };
        }
        
        /// <summary>
        /// 현재 활성화된 기능 목록 반환
        /// </summary>
        public string[] GetActiveFeatures()
        {
            var features = new System.Collections.Generic.List<string>();
            
            if (_currentSettings.IsFaceBlurEnabled) features.Add("얼굴 흐림");
            if (_currentSettings.IsFullBodyBlurEnabled) features.Add("몸 전체 흐림");
            if (_currentSettings.IsHelmetMonitoringEnabled) features.Add("안전모 감시");
            if (_currentSettings.IsGlovesMonitoringEnabled) features.Add("안전장갑 감시");
            if (_currentSettings.IsSafetyBootsMonitoringEnabled) features.Add("안전화 감시");
            if (_currentSettings.IsDangerZoneMonitoringEnabled) features.Add("위험구역 감시");
            if (_currentSettings.IsWarningZoneMonitoringEnabled) features.Add("경고구역 감시");
            if (_currentSettings.IsFallDetectionEnabled) features.Add("낙하 감지");
            if (_currentSettings.IsEmergencySignalDetectionEnabled) features.Add("구조 신호 감지");
            if (_currentSettings.IsTwoPersonWorkMonitoringEnabled) features.Add("2인 1조 작업 감시");
            if (_currentSettings.IsWorkerDensityMonitoringEnabled) features.Add("작업자 밀집 감시");
            if (_currentSettings.IsExternalSoundAlertEnabled) features.Add("외부 소리 알림");
            
            return features.ToArray();
        }
    }
}