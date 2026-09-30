namespace lambdaflow.lambdaflow.Core
{
    internal static class ReservedMessageKinds
    {
        internal const string Ready = "__lambdaflow_ready";
        internal const string Window = "__lambdaflow_window";
        internal const string Close = "__lambdaflow_close";
        internal const string Console = "__console";
    }

    internal static class WindowLimits
    {
        internal const int MinWidth = 320;
        internal const int MinHeight = 240;
        internal const int MaxWidth = 8192;
        internal const int MaxHeight = 8192;

        internal static int ClampWidth(int width) => Math.Clamp(width, MinWidth, MaxWidth);
        internal static int ClampHeight(int height) => Math.Clamp(height, MinHeight, MaxHeight);
        internal static int ClampOptionalWidth(int width) => width <= 0 ? 0 : ClampWidth(width);
        internal static int ClampOptionalHeight(int height) => height <= 0 ? 0 : ClampHeight(height);
    }
}
