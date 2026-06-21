using System;

namespace CodexUsageMonitorV2
{
    internal enum AppStatusKind
    {
        Information,
        Success,
        Warning,
        Error
    }

    internal sealed class AppStatusEventArgs : EventArgs
    {
        public AppStatusEventArgs(AppStatusKind kind, string message, bool notify)
        {
            Kind = kind;
            Message = message;
            Notify = notify;
        }

        public AppStatusKind Kind { get; private set; }
        public string Message { get; private set; }
        public bool Notify { get; private set; }
    }
}
