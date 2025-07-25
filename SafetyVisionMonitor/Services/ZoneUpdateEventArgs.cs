using System;
using SafetyVisionMonitor.Models;

namespace SafetyVisionMonitor.Services
{
    public class ZoneUpdateEventArgs : EventArgs
    {
        public string ZoneId { get; }
        public bool IsEnabled { get; }
        public Zone3D Zone { get; }

        public ZoneUpdateEventArgs(Zone3D zone)
        {
            ZoneId = zone.Id;
            IsEnabled = zone.IsEnabled;
            Zone = zone;
        }
    }
}