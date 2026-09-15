using System.Drawing;
using System.Windows.Forms;

namespace CodexUsageOverlay
{
    internal enum OverlayMouseAction
    {
        None,
        ExitApplication
    }

    internal static class OverlayInteraction
    {
        internal static int GetCenteredOverlayLeft(int windowLeft, int windowWidth, int overlayWidth)
        {
            return windowLeft + (windowWidth - overlayWidth) / 2;
        }

        internal static Rectangle GetMainUsageBounds(
            int resetRadarLeft,
            int headerHeight,
            int leftMargin = 10,
            int rightGap = 4)
        {
            return new Rectangle(
                leftMargin,
                0,
                System.Math.Max(40, resetRadarLeft - leftMargin - rightGap),
                System.Math.Max(1, headerHeight - 2));
        }

        internal static OverlayMouseAction DecideMouseUp(
            MouseButtons button,
            Point logicalLocation,
            Rectangle mainUsageBounds,
            bool rightDownStartedInMainUsage)
        {
            return button == MouseButtons.Right && rightDownStartedInMainUsage &&
                mainUsageBounds.Contains(logicalLocation)
                ? OverlayMouseAction.ExitApplication
                : OverlayMouseAction.None;
        }
    }
}
