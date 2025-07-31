using System;
using System.Windows;
using System.Windows.Media;

namespace SafetyVisionMonitor.Services
{
    /// <summary>
    /// 좌표 변환을 통합 관리하는 서비스
    /// Image의 Stretch="Uniform" 속성을 고려한 정확한 좌표 매핑
    /// </summary>
    public class CoordinateMapper
    {
        private readonly double _imageWidth;
        private readonly double _imageHeight;
        private double _renderWidth;
        private double _renderHeight;
        private double _offsetX;
        private double _offsetY;
        private double _scale;

        public CoordinateMapper(double imageWidth, double imageHeight)
        {
            _imageWidth = imageWidth;
            _imageHeight = imageHeight;
        }

        /// <summary>
        /// 캔버스(컨테이너) 크기가 변경될 때 호출
        /// Stretch="Uniform"을 고려한 실제 렌더링 영역 계산
        /// </summary>
        public void UpdateCanvasSize(double canvasWidth, double canvasHeight)
        {
            if (canvasWidth <= 0 || canvasHeight <= 0) return;

            // Aspect ratio 계산
            var imageAspect = _imageWidth / _imageHeight;
            var canvasAspect = canvasWidth / canvasHeight;

            if (imageAspect > canvasAspect)
            {
                // 이미지가 더 넓음 - 너비에 맞춤
                _scale = canvasWidth / _imageWidth;
                _renderWidth = canvasWidth;
                _renderHeight = _imageHeight * _scale;
                _offsetX = 0;
                _offsetY = (canvasHeight - _renderHeight) / 2;
            }
            else
            {
                // 이미지가 더 높음 - 높이에 맞춤
                _scale = canvasHeight / _imageHeight;
                _renderWidth = _imageWidth * _scale;
                _renderHeight = canvasHeight;
                _offsetX = (canvasWidth - _renderWidth) / 2;
                _offsetY = 0;
            }
        }

        /// <summary>
        /// 캔버스 좌표를 이미지 좌표로 변환
        /// </summary>
        public Point CanvasToImage(Point canvasPoint)
        {
            // 렌더링 영역 내부인지 확인
            if (canvasPoint.X < _offsetX || canvasPoint.X > _offsetX + _renderWidth ||
                canvasPoint.Y < _offsetY || canvasPoint.Y > _offsetY + _renderHeight)
            {
                // 영역 밖의 점은 가장 가까운 경계로 클램핑
                var clampedX = Math.Max(_offsetX, Math.Min(_offsetX + _renderWidth, canvasPoint.X));
                var clampedY = Math.Max(_offsetY, Math.Min(_offsetY + _renderHeight, canvasPoint.Y));
                canvasPoint = new Point(clampedX, clampedY);
            }

            // 오프셋 제거 및 스케일 복원
            var imageX = (canvasPoint.X - _offsetX) / _scale;
            var imageY = (canvasPoint.Y - _offsetY) / _scale;

            // 이미지 경계 내로 클램핑
            imageX = Math.Max(0, Math.Min(_imageWidth - 1, imageX));
            imageY = Math.Max(0, Math.Min(_imageHeight - 1, imageY));

            return new Point(imageX, imageY);
        }

        /// <summary>
        /// 이미지 좌표를 캔버스 좌표로 변환
        /// </summary>
        public Point ImageToCanvas(Point imagePoint)
        {
            var canvasX = imagePoint.X * _scale + _offsetX;
            var canvasY = imagePoint.Y * _scale + _offsetY;
            return new Point(canvasX, canvasY);
        }

        /// <summary>
        /// 상대 좌표(0~1)를 이미지 좌표로 변환
        /// </summary>
        public Point RelativeToImage(Point relativePoint)
        {
            var imageX = relativePoint.X * _imageWidth;
            var imageY = relativePoint.Y * _imageHeight;
            return new Point(imageX, imageY);
        }

        /// <summary>
        /// 이미지 좌표를 상대 좌표(0~1)로 변환
        /// </summary>
        public Point ImageToRelative(Point imagePoint)
        {
            var relativeX = imagePoint.X / _imageWidth;
            var relativeY = imagePoint.Y / _imageHeight;
            return new Point(relativeX, relativeY);
        }

        /// <summary>
        /// 캔버스 좌표를 상대 좌표(0~1)로 직접 변환
        /// </summary>
        public Point CanvasToRelative(Point canvasPoint)
        {
            var imagePoint = CanvasToImage(canvasPoint);
            return ImageToRelative(imagePoint);
        }

        /// <summary>
        /// 상대 좌표(0~1)를 캔버스 좌표로 직접 변환
        /// </summary>
        public Point RelativeToCanvas(Point relativePoint)
        {
            var imagePoint = RelativeToImage(relativePoint);
            return ImageToCanvas(imagePoint);
        }

        /// <summary>
        /// 현재 렌더링 영역 정보 반환
        /// </summary>
        public Rect GetRenderBounds()
        {
            return new Rect(_offsetX, _offsetY, _renderWidth, _renderHeight);
        }

        /// <summary>
        /// 캔버스 좌표가 이미지 렌더링 영역 내에 있는지 확인
        /// </summary>
        public bool IsPointInRenderArea(Point canvasPoint)
        {
            return canvasPoint.X >= _offsetX && 
                   canvasPoint.X <= _offsetX + _renderWidth &&
                   canvasPoint.Y >= _offsetY && 
                   canvasPoint.Y <= _offsetY + _renderHeight;
        }
    }
}