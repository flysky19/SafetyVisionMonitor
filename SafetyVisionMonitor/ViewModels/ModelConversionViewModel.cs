using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SafetyVisionMonitor.ViewModels.Base;

namespace SafetyVisionMonitor.ViewModels
{
    public partial class ModelConversionViewModel : BaseViewModel
    {
        [ObservableProperty]
        private string inputFilePath = string.Empty;
        
        [ObservableProperty]
        private string outputFilePath = string.Empty;
        
        [ObservableProperty]
        private string selectedInputSize = "640x640 (기본)";
        
        [ObservableProperty]
        private int batchSize = 1;
        
        [ObservableProperty]
        private bool useDynamicAxes = false; // YoloDotNet은 동적 축을 지원하지 않음
        
        [ObservableProperty]
        private bool applyOptimization = true;
        
        [ObservableProperty]
        private bool useFP16 = false;
        
        [ObservableProperty]
        private bool isConverting = false;
        
        [ObservableProperty]
        private double conversionProgress = 0;
        
        [ObservableProperty]
        private string conversionStatus = "준비됨";
        
        [ObservableProperty]
        private string conversionLog = string.Empty;
        
        [ObservableProperty]
        private bool canConvert = false;
        
        private CancellationTokenSource? _cancellationTokenSource;
        private Process? _conversionProcess;
        
        public ModelConversionViewModel()
        {
            Title = "모델 변환";
            UpdateCanConvert();
        }
        
        partial void OnInputFilePathChanged(string value)
        {
            UpdateCanConvert();
            UpdateOutputFilePath();
        }
        
        partial void OnOutputFilePathChanged(string value)
        {
            UpdateCanConvert();
        }
        
        private void UpdateCanConvert()
        {
            CanConvert = !IsConverting && 
                        !string.IsNullOrEmpty(InputFilePath) && 
                        !string.IsNullOrEmpty(OutputFilePath) && 
                        File.Exists(InputFilePath);
        }
        
        private void UpdateOutputFilePath()
        {
            if (!string.IsNullOrEmpty(InputFilePath) && string.IsNullOrEmpty(OutputFilePath))
            {
                var directory = Path.GetDirectoryName(InputFilePath);
                var fileName = Path.GetFileNameWithoutExtension(InputFilePath);
                OutputFilePath = Path.Combine(directory ?? "", $"{fileName}.onnx");
            }
        }
        
        [RelayCommand]
        private void BrowseInputFile()
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "PyTorch 모델 파일 선택",
                Filter = "PyTorch 모델 (*.pt)|*.pt|모든 파일 (*.*)|*.*",
                CheckFileExists = true
            };
            
            if (openFileDialog.ShowDialog() == true)
            {
                InputFilePath = openFileDialog.FileName;
                AppendLog($"입력 파일 선택: {InputFilePath}");
            }
        }
        
        [RelayCommand]
        private void BrowseOutputFile()
        {
            var saveFileDialog = new SaveFileDialog
            {
                Title = "ONNX 파일 저장 위치",
                Filter = "ONNX 모델 (*.onnx)|*.onnx|모든 파일 (*.*)|*.*",
                DefaultExt = "onnx"
            };
            
            if (saveFileDialog.ShowDialog() == true)
            {
                OutputFilePath = saveFileDialog.FileName;
                AppendLog($"출력 파일 설정: {OutputFilePath}");
            }
        }
        
        [RelayCommand]
        private async Task ConvertModel()
        {
            if (!CanConvert) return;
            
            try
            {
                IsConverting = true;
                ConversionProgress = 0;
                ConversionStatus = "변환 시작 중...";
                ConversionLog = string.Empty;
                _cancellationTokenSource = new CancellationTokenSource();
                
                AppendLog("=== YOLO 모델 변환 시작 ===");
                AppendLog($"입력 파일: {InputFilePath}");
                AppendLog($"출력 파일: {OutputFilePath}");
                AppendLog($"입력 크기: {GetInputSizeValue()}");
                AppendLog($"배치 크기: {BatchSize}");
                AppendLog($"동적 축: {(UseDynamicAxes ? "사용" : "미사용")}");
                AppendLog($"최적화: {(ApplyOptimization ? "적용" : "미적용")}");
                AppendLog($"FP16: {(UseFP16 ? "사용" : "미사용")}");
                AppendLog("");
                
                // Python 스크립트 경로 확인
                var scriptPath = await EnsureConversionScript();
                if (string.IsNullOrEmpty(scriptPath))
                {
                    throw new Exception("변환 스크립트를 생성할 수 없습니다.");
                }
                
                // Python 실행 파일 찾기
                var pythonPath = FindPythonExecutable();
                if (string.IsNullOrEmpty(pythonPath))
                {
                    throw new Exception("Python 실행 파일을 찾을 수 없습니다. Python이 설치되어 있는지 확인하세요.");
                }
                
                AppendLog($"Python 경로: {pythonPath}");
                AppendLog($"스크립트 경로: {scriptPath}");
                AppendLog("");
                
                // 변환 실행
                await RunConversion(pythonPath, scriptPath);
                
                if (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    ConversionProgress = 100;
                    ConversionStatus = "변환 완료!";
                    AppendLog("=== 변환 성공적으로 완료 ===");
                    
                    MessageBox.Show("모델 변환이 성공적으로 완료되었습니다!", "변환 완료", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                ConversionStatus = "변환 실패";
                AppendLog($"오류 발생: {ex.Message}");
                AppendLog("=== 변환 실패 ===");
                
                MessageBox.Show($"모델 변환 중 오류가 발생했습니다:\n{ex.Message}", "변환 오류", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsConverting = false;
                _conversionProcess?.Dispose();
                _conversionProcess = null;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }
        
        [RelayCommand]
        private void CancelConversion()
        {
            if (IsConverting)
            {
                _cancellationTokenSource?.Cancel();
                _conversionProcess?.Kill();
                ConversionStatus = "변환 취소됨";
                AppendLog("사용자가 변환을 취소했습니다.");
            }
        }
        
        private async Task<string> EnsureConversionScript()
        {
            var scriptDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts");
            Directory.CreateDirectory(scriptDirectory);
            
            var scriptPath = Path.Combine(scriptDirectory, "convert_yolo.py");
            
            var scriptContent = @"#!/usr/bin/env python3
""""""
YOLO PyTorch to ONNX Conversion Script
""""""

import sys
import os
import argparse
from pathlib import Path

def install_requirements():
    """"""필요한 패키지 설치""""""
    try:
        import torch
        import ultralytics
        print(""필요한 패키지가 이미 설치되어 있습니다."")
        
        # ONNX 패키지 확인 (선택사항)
        try:
            import onnx
            import onnxruntime
            print(""ONNX 관련 패키지도 사용 가능합니다."")
        except ImportError:
            print(""ONNX 패키지가 없지만 ultralytics가 자동으로 처리할 수 있습니다."")
        
        return True
    except ImportError as e:
        print(f""필요한 패키지를 설치하는 중... (이 과정은 시간이 걸릴 수 있습니다)"")
        try:
            import subprocess
            import os
            
            # 환경 변수 설정으로 경고 억제
            env = os.environ.copy()
            env['PYTHONWARNINGS'] = 'ignore'
            
            # Python 3.13 환경에서 일반적인 방식으로 설치
            print(""torch와 ultralytics 패키지 설치 중..."")
            subprocess.check_call([
                sys.executable, '-m', 'pip', 'install', 
                '--no-warn-script-location',
                '--disable-pip-version-check',
                '--upgrade',
                'torch', 'torchvision', 'ultralytics'
            ], env=env)
            
            print(""ONNX 관련 패키지 설치 시도 중..."")
            try:
                # ONNX 패키지 별도 설치 시도
                subprocess.check_call([
                    sys.executable, '-m', 'pip', 'install', 
                    '--no-warn-script-location',
                    '--disable-pip-version-check',
                    'onnx', 'onnxruntime'
                ], env=env)
                print(""ONNX 패키지 설치 완료"")
            except subprocess.CalledProcessError:
                print(""ONNX 패키지 설치 실패, ultralytics 내장 기능을 사용합니다."")
            
            print(""패키지 설치가 완료되었습니다."")
            return True
        except subprocess.CalledProcessError as e:
            print(f""패키지 설치 실패: {e}"")
            print(""Microsoft Store Python에서 경로 길이 제한으로 인한 설치 실패입니다."")
            print(""해결 방법:"")
            print(""1. 일반 Python 설치 (https://python.org)를 권장합니다."")
            print(""2. 또는 관리자 권한으로 애플리케이션을 실행해보세요."")
            print(""3. 기존에 설치된 패키지만으로 변환을 시도해볼 수 있습니다."")
            return False
        except Exception as install_error:
            print(f""패키지 설치 실패: {install_error}"")
            return False

def convert_model(input_path, output_path, imgsz, batch_size, dynamic, optimize, half):
    """"""YOLO 모델을 ONNX로 변환""""""
    try:
        from ultralytics import YOLO
        
        print(f""모델 로딩 중: {input_path}"")
        model = YOLO(input_path)
        
        print(""ONNX 변환 시작..."")
        print(""참고: ONNX 패키지가 없어도 ultralytics가 자동으로 처리합니다."")
        
        # YoloDotNet 호환성을 위해 옵션 조정
        print(""주의: YoloDotNet은 동적 축을 지원하지 않으므로 dynamic=False로 설정합니다."")
        success = model.export(
            format='onnx',
            imgsz=imgsz,
            batch=batch_size,
            dynamic=False,   # YoloDotNet 호환성을 위해 항상 False
            optimize=False,  # 최적화 비활성화 (호환성 문제 방지)
            half=False,      # FP32 유지 (호환성 문제 방지)
            simplify=True    # 모델 단순화 (권장)
        )
        
        # 생성된 파일을 지정된 위치로 이동
        generated_file = str(Path(input_path).with_suffix('.onnx'))
        if os.path.exists(generated_file) and generated_file != output_path:
            import shutil
            shutil.move(generated_file, output_path)
            print(f""파일 이동 완료: {output_path}"")
        
        print(""변환 완료!"")
        return True
        
    except Exception as e:
        print(f""변환 중 오류 발생: {e}"")
        return False

def main():
    parser = argparse.ArgumentParser(description='YOLO PyTorch to ONNX Converter')
    parser.add_argument('--input', required=True, help='Input PyTorch model path (.pt)')
    parser.add_argument('--output', required=True, help='Output ONNX model path (.onnx)')
    parser.add_argument('--imgsz', type=int, default=640, help='Image size (default: 640)')
    parser.add_argument('--batch', type=int, default=1, help='Batch size (default: 1)')
    parser.add_argument('--dynamic', action='store_true', help='Enable dynamic axes')
    parser.add_argument('--optimize', action='store_true', help='Apply optimization')
    parser.add_argument('--half', action='store_true', help='Use FP16 precision')
    
    args = parser.parse_args()
    
    print(""=== YOLO PyTorch to ONNX Converter ==="" )
    print(f""입력 파일: {args.input}"")
    print(f""출력 파일: {args.output}"")
    print(f""이미지 크기: {args.imgsz}"")
    print(f""배치 크기: {args.batch}"")
    print(f""동적 축: {args.dynamic}"")
    print(f""최적화: {args.optimize}"")
    print(f""FP16: {args.half}"")
    print()
    
    # 입력 파일 확인
    if not os.path.exists(args.input):
        print(f""오류: 입력 파일이 존재하지 않습니다: {args.input}"")
        return 1
    
    # 필요한 패키지 설치
    if not install_requirements():
        print(""필요한 패키지를 설치할 수 없습니다."")
        return 1
    
    # 변환 실행
    if convert_model(args.input, args.output, args.imgsz, args.batch, args.dynamic, args.optimize, args.half):
        print(""변환이 성공적으로 완료되었습니다!"")
        return 0
    else:
        print(""변환 중 오류가 발생했습니다."")
        return 1

if __name__ == '__main__':
    sys.exit(main())
";
            
            await File.WriteAllTextAsync(scriptPath, scriptContent);
            AppendLog($"변환 스크립트 생성: {scriptPath}");
            
            return scriptPath;
        }
        
        private string FindPythonExecutable()
        {
            var pythonPaths = new[]
            {
                // 새로 설치된 Python 3.13을 우선적으로 찾기
                @"C:\Python313\python.exe",
                @"C:\Python31\python.exe", 
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Python313\python.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Python\Python313\python.exe"),
                
                // py launcher 사용 (최신 버전 자동 선택)
                "py",
                "python",
                "python3",
                
                // 기타 Python 버전들
                @"C:\Python312\python.exe",
                @"C:\Python311\python.exe",
                @"C:\Python310\python.exe",
                @"C:\Python39\python.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Python\Python312\python.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Python\Python311\python.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Python\Python310\python.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Python\Python39\python.exe")
            };
            
            foreach (var pythonPath in pythonPaths)
            {
                try
                {
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = pythonPath,
                            Arguments = "--version",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };
                    
                    process.Start();
                    process.WaitForExit(5000);
                    
                    if (process.ExitCode == 0)
                    {
                        var output = process.StandardOutput.ReadToEnd();
                        if (output.Contains("Python"))
                        {
                            // Microsoft Store Python 확인 및 경고
                            if (pythonPath.Contains("WindowsApps") || pythonPath.Contains("Microsoft"))
                            {
                                AppendLog($"Microsoft Store Python 발견: {pythonPath} - {output.Trim()}");
                                AppendLog("경고: Microsoft Store Python은 권한 문제가 있을 수 있습니다.");
                                // Microsoft Store Python은 마지막 선택지로 사용
                                continue;
                            }
                            
                            AppendLog($"Python 발견: {pythonPath} - {output.Trim()}");
                            return pythonPath;
                        }
                    }
                }
                catch
                {
                    // 무시하고 다음 경로 시도
                }
            }
            
            return string.Empty;
        }
        
        private async Task RunConversion(string pythonPath, string scriptPath)
        {
            var inputSize = GetInputSizeValue();
            var arguments = $"\"{scriptPath}\" " +
                          $"--input \"{InputFilePath}\" " +
                          $"--output \"{OutputFilePath}\" " +
                          $"--imgsz {inputSize} " +
                          $"--batch {BatchSize}";
            
            if (UseDynamicAxes) arguments += " --dynamic";
            if (ApplyOptimization) arguments += " --optimize";
            if (UseFP16) arguments += " --half";
            
            _conversionProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = pythonPath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(InputFilePath)
                }
            };
            
            _conversionProcess.OutputDataReceived += OnOutputReceived;
            _conversionProcess.ErrorDataReceived += OnErrorReceived;
            
            AppendLog($"실행 명령: {pythonPath} {arguments}");
            AppendLog("");
            
            ConversionStatus = "변환 중...";
            ConversionProgress = 10;
            
            _conversionProcess.Start();
            _conversionProcess.BeginOutputReadLine();
            _conversionProcess.BeginErrorReadLine();
            
            // 진행률 시뮬레이션 (실제 진행률을 알기 어려우므로)
            var progressTask = Task.Run(async () =>
            {
                var progress = 10.0;
                while (!_conversionProcess.HasExited && !_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    await Task.Delay(1000, _cancellationTokenSource.Token);
                    progress = Math.Min(90, progress + 5);
                    ConversionProgress = progress;
                }
            });
            
            await _conversionProcess.WaitForExitAsync();
            
            if (_conversionProcess.ExitCode != 0)
            {
                throw new Exception($"변환 프로세스가 오류 코드 {_conversionProcess.ExitCode}로 종료되었습니다.");
            }
            
            // 출력 파일 존재 확인
            if (!File.Exists(OutputFilePath))
            {
                throw new Exception("변환된 ONNX 파일을 찾을 수 없습니다.");
            }
        }
        
        private void OnOutputReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    AppendLog($"[출력] {e.Data}");
                });
            }
        }
        
        private void OnErrorReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    AppendLog($"[오류] {e.Data}");
                });
            }
        }
        
        private int GetInputSizeValue()
        {
            return SelectedInputSize switch
            {
                "416x416" => 416,
                "512x512" => 512,
                "1024x1024" => 1024,
                _ => 640
            };
        }
        
        private void AppendLog(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            ConversionLog += $"[{timestamp}] {message}\n";
        }
    }
}