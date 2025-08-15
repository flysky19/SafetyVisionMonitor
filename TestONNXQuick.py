#!/usr/bin/env python3
"""
ONNX 모델 빠른 검사 도구
Python ONNX 라이브러리를 사용하여 모델 파일의 유효성을 확인합니다.
"""

import os
import onnx
import onnxruntime as ort
from pathlib import Path

def check_onnx_model(model_path):
    """ONNX 모델 파일 검사"""
    print(f"\n=== {os.path.basename(model_path)} ===")
    
    if not os.path.exists(model_path):
        print("❌ 파일이 존재하지 않습니다")
        return False
    
    # 파일 크기 확인
    file_size = os.path.getsize(model_path) / (1024 * 1024)  # MB
    print(f"파일 크기: {file_size:.2f} MB")
    
    try:
        # ONNX 모델 로드 및 검증
        print("ONNX 모델 검증 중...")
        model = onnx.load(model_path)
        onnx.checker.check_model(model)
        print("✅ ONNX 모델 구조 유효")
        
        # 입력/출력 정보
        graph = model.graph
        print(f"모델 프로듀서: {model.producer_name}")
        print(f"모델 버전: {model.model_version}")
        
        print("\n[입력 정보]")
        for input_tensor in graph.input:
            shape = [dim.dim_value if dim.dim_value > 0 else 'dynamic' for dim in input_tensor.type.tensor_type.shape.dim]
            print(f"  {input_tensor.name}: {shape}")
        
        print("\n[출력 정보]")
        for output_tensor in graph.output:
            shape = [dim.dim_value if dim.dim_value > 0 else 'dynamic' for dim in output_tensor.type.tensor_type.shape.dim]
            print(f"  {output_tensor.name}: {shape}")
            
        # ONNX Runtime으로 세션 생성 테스트
        print("\nONNX Runtime 세션 생성 테스트...")
        try:
            session = ort.InferenceSession(model_path, providers=['CPUExecutionProvider'])
            input_info = session.get_inputs()[0]
            output_info = session.get_outputs()[0]
            
            print(f"✅ 세션 생성 성공")
            print(f"  입력: {input_info.name} {input_info.shape}")
            print(f"  출력: {output_info.name} {output_info.shape}")
            
            # YoloDotNet 호환성 체크
            is_compatible = (input_info.name == "images" and 
                           len(input_info.shape) == 4 and 
                           all(dim > 0 for dim in input_info.shape))
            print(f"YoloDotNet 호환성: {'✅' if is_compatible else '❌'}")
            
            return True
            
        except Exception as rt_ex:
            print(f"❌ ONNX Runtime 오류: {rt_ex}")
            return False
            
    except Exception as ex:
        print(f"❌ ONNX 검증 실패: {ex}")
        return False

def main():
    """메인 함수"""
    print("=== ONNX 모델 빠른 검사 ===")
    
    models_dir = "/mnt/d/000.Source/SafetyVisionMonitor/SafetyVisionMonitor/bin/Debug/net8.0-windows/Models"
    
    if not os.path.exists(models_dir):
        print(f"❌ 모델 디렉토리가 존재하지 않습니다: {models_dir}")
        return
    
    model_files = [
        "yolov8s.onnx",
        "yolov8s-pose.onnx", 
        "yolov8s-seg.onnx",
        "olov8s-cls.onnx",  # 오타 주의
        "yolov8s-obb.onnx"
    ]
    
    valid_models = 0
    total_models = 0
    
    for model_file in model_files:
        model_path = os.path.join(models_dir, model_file)
        total_models += 1
        
        if check_onnx_model(model_path):
            valid_models += 1
    
    print(f"\n=== 검사 결과 ===")
    print(f"총 {total_models}개 모델 중 {valid_models}개 유효")
    
    if valid_models < total_models:
        print("\n⚠️ 문제가 있는 모델이 있습니다!")
        print("해결 방법:")
        print("1. 모델을 다시 변환해보세요")
        print("2. 다른 변환 설정을 시도해보세요")
        print("3. 공식 Ultralytics 모델을 다운로드해보세요")

if __name__ == "__main__":
    main()