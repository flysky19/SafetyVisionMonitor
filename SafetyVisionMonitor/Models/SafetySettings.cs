using System.ComponentModel;

namespace SafetyVisionMonitor.Models
{
    /// <summary>
    /// 안전 모니터링 시스템의 모든 기능 설정을 관리하는 모델
    /// </summary>
    public class SafetySettings : INotifyPropertyChanged
    {
        #region 1. 안전보호구 감시 기능
        
        /// <summary>
        /// 안전모 감시 기능 사용 여부
        /// </summary>
        public bool IsHelmetMonitoringEnabled { get; set; } = true;
        
        /// <summary>
        /// 안전장갑 감시 기능 사용 여부
        /// </summary>
        public bool IsGlovesMonitoringEnabled { get; set; } = false;
        
        /// <summary>
        /// 안전화 감시 기능 사용 여부 (화면에서 감지위치 박스 처리)
        /// </summary>
        public bool IsSafetyBootsMonitoringEnabled { get; set; } = false;
        
        #endregion
        
        #region 2. 개인정보 보호 기능
        
        /// <summary>
        /// 얼굴 흐림 처리 기능
        /// </summary>
        public bool IsFaceBlurEnabled { get; set; } = false;
        
        /// <summary>
        /// 사람 몸 전체 흐림 처리 기능
        /// </summary>
        public bool IsFullBodyBlurEnabled { get; set; } = false;
        
        #endregion
        
        #region 3. 위험, 경고 감시 구역 설정 기능
        
        /// <summary>
        /// 위험 구역 설정 기능
        /// </summary>
        public bool IsDangerZoneMonitoringEnabled { get; set; } = true;
        
        /// <summary>
        /// 경고 구역 설정 기능
        /// </summary>
        public bool IsWarningZoneMonitoringEnabled { get; set; } = true;
        
        #endregion
        
        #region 4. 작업자 불안전 행동 감시
        
        /// <summary>
        /// 2인 1조 작업 미준수 감시 기능
        /// </summary>
        public bool IsTwoPersonWorkMonitoringEnabled { get; set; } = false;
        
        /// <summary>
        /// 낙하 사고 감시 기능
        /// </summary>
        public bool IsFallDetectionEnabled { get; set; } = false;
        
        /// <summary>
        /// 구조 신호 감시 기능
        /// </summary>
        public bool IsEmergencySignalDetectionEnabled { get; set; } = false;
        
        #endregion
        
        #region 5. 안전 모니터링 구역 설정 기능
        
        /// <summary>
        /// 안전 모니터링 구역 설정 기능 사용 여부
        /// 설명: 해당 구역 설정한 구역만 모니터링하며, 그 외 구역은 사람 및 객체를 감지, 구역을 SKIP 함
        /// </summary>
        public bool IsSafetyMonitoringZoneEnabled { get; set; } = false;
        
        #endregion
        
        #region 6. 안전 모니터링 구역 작업자 관리 기능
        
        /// <summary>
        /// 작업자 밀집 인원 기능 사용 여부
        /// </summary>
        public bool IsWorkerDensityMonitoringEnabled { get; set; } = false;
        
        /// <summary>
        /// 밀집 인원 설정 (명)
        /// </summary>
        public int MaxWorkerDensityCount { get; set; } = 5;
        
        #endregion
        
        #region 7. 외부 연동 기능
        
        /// <summary>
        /// 외부 소리 알림 기능
        /// </summary>
        public bool IsExternalSoundAlertEnabled { get; set; } = false;
        
        /// <summary>
        /// MQTT 알림 및 데이터 연동 기능 (미래 구현)
        /// </summary>
        public bool IsMqttIntegrationEnabled { get; set; } = false;
        
        /// <summary>
        /// 미쯔비시 PLC 연동 (미래 구현)
        /// </summary>
        public bool IsMitsubishiPlcIntegrationEnabled { get; set; } = false;
        
        /// <summary>
        /// Safety PLC 연동 (미래 구현)
        /// </summary>
        public bool IsSafetyPlcIntegrationEnabled { get; set; } = false;
        
        /// <summary>
        /// CC-Link 연동 (미래 구현)
        /// </summary>
        public bool IsCcLinkIntegrationEnabled { get; set; } = false;
        
        #endregion
        
        #region MQTT 설정 (미래 구현용)
        
        public string MqttBrokerAddress { get; set; } = "localhost";
        public int MqttPort { get; set; } = 1883;
        public string MqttClientId { get; set; } = "SafetyVisionMonitor";
        public string MqttUsername { get; set; } = "";
        public string MqttPassword { get; set; } = "";
        
        #endregion
        
        #region PLC 설정 (미래 구현용)
        
        public string PlcIpAddress { get; set; } = "192.168.1.100";
        public int PlcPort { get; set; } = 502;
        public int PlcStationNumber { get; set; } = 1;
        
        #endregion
        
        #region 외부 알림 설정
        
        /// <summary>
        /// 외부 사운드 파일 경로
        /// </summary>
        public string ExternalSoundFilePath { get; set; } = "";
        
        /// <summary>
        /// 알림 음량 (0-100)
        /// </summary>
        public int AlertVolume { get; set; } = 80;
        
        #endregion
        
        #region INotifyPropertyChanged Implementation
        
        public event PropertyChangedEventHandler? PropertyChanged;
        
        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        
        #endregion
        
        /// <summary>
        /// 설정의 기본값으로 초기화
        /// </summary>
        public void ResetToDefaults()
        {
            // 1. 안전보호구 감시 기능
            IsHelmetMonitoringEnabled = true;
            IsGlovesMonitoringEnabled = false;
            IsSafetyBootsMonitoringEnabled = false;
            
            // 2. 개인정보 보호 기능
            IsFaceBlurEnabled = false;
            IsFullBodyBlurEnabled = false;
            
            // 3. 위험, 경고 감시 구역 설정 기능
            IsDangerZoneMonitoringEnabled = true;
            IsWarningZoneMonitoringEnabled = true;
            
            // 4. 작업자 불안전 행동 감시
            IsTwoPersonWorkMonitoringEnabled = false;
            IsFallDetectionEnabled = false;
            IsEmergencySignalDetectionEnabled = false;
            
            // 5. 안전 모니터링 구역 설정 기능
            IsSafetyMonitoringZoneEnabled = false;
            
            // 6. 안전 모니터링 구역 작업자 관리 기능
            IsWorkerDensityMonitoringEnabled = false;
            MaxWorkerDensityCount = 5;
            
            // 7. 외부 연동 기능
            IsExternalSoundAlertEnabled = false;
            IsMqttIntegrationEnabled = false;
            IsMitsubishiPlcIntegrationEnabled = false;
            IsSafetyPlcIntegrationEnabled = false;
            IsCcLinkIntegrationEnabled = false;
            
            // 기타 설정
            AlertVolume = 80;
        }
        
        /// <summary>
        /// 설정 유효성 검사
        /// </summary>
        public bool ValidateSettings(out string errorMessage)
        {
            errorMessage = "";
            
            if (MaxWorkerDensityCount < 1 || MaxWorkerDensityCount > 100)
            {
                errorMessage = "밀집 인원 설정은 1명 이상 100명 이하여야 합니다.";
                return false;
            }
            
            if (AlertVolume < 0 || AlertVolume > 100)
            {
                errorMessage = "알림 음량은 0에서 100 사이여야 합니다.";
                return false;
            }
            
            if (MqttPort < 1 || MqttPort > 65535)
            {
                errorMessage = "MQTT 포트는 1에서 65535 사이여야 합니다.";
                return false;
            }
            
            if (PlcPort < 1 || PlcPort > 65535)
            {
                errorMessage = "PLC 포트는 1에서 65535 사이여야 합니다.";
                return false;
            }
            
            return true;
        }
    }
}