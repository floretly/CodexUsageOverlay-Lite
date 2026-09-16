using System;
using System.Drawing;
using System.Windows.Forms;

namespace CodexUsageOverlay
{
    internal static class OverlayInteractionTests
    {
        public static void OverlayStaysCentered()
        {
            int left = OverlayInteraction.GetCenteredOverlayLeft(100, 1200, 568);
            Assert(left == 416, "overlay was not centered in the target window");
            Assert(left - 100 == (100 + 1200) - (left + 568),
                "overlay side margins were not equal");
        }

        public static void HeaderContentStaysCentered()
        {
            int left = OverlayInteraction.GetCenteredContentLeft(720, 654);
            int right = 720 - (left + 654);
            Assert(left == 33, "header content did not start at the centered position");
            Assert(left == right, "header content side margins were not equal");
        }

        public static void HeaderContentSupportsOpticalRightOffset()
        {
            int centeredLeft = OverlayInteraction.GetCenteredContentLeft(720, 654);
            int offsetLeft = OverlayInteraction.GetCenteredContentLeft(720, 654, 26);
            Assert(offsetLeft - centeredLeft == 26,
                "header content did not apply the requested optical right offset");
        }

        public static void RightClickMainUsageRequestsExit()
        {
            Rectangle bounds = OverlayInteraction.GetMainUsageBounds(500, 28);
            Point center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);

            Assert(OverlayInteraction.DecideMouseUp(MouseButtons.Right, center, bounds, true) ==
                OverlayMouseAction.ExitApplication, "right click did not request exit");
        }

        public static void OtherButtonsDoNotRequestExit()
        {
            Rectangle bounds = OverlayInteraction.GetMainUsageBounds(500, 28);
            Point center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);

            Assert(OverlayInteraction.DecideMouseUp(MouseButtons.Left, center, bounds, true) ==
                OverlayMouseAction.None, "left click requested exit");
            Assert(OverlayInteraction.DecideMouseUp(MouseButtons.Middle, center, bounds, true) ==
                OverlayMouseAction.None, "middle click requested exit");
        }

        public static void RightClickOutsideMainUsageDoesNotRequestExit()
        {
            Rectangle bounds = OverlayInteraction.GetMainUsageBounds(500, 28);
            Point[] outside =
            {
                new Point(bounds.Left - 1, bounds.Top + 1),
                new Point(bounds.Right, bounds.Top + 1),
                new Point(bounds.Left + 1, bounds.Bottom),
                new Point(bounds.Right + 8, bounds.Top + bounds.Height / 2),
                new Point(bounds.Left + bounds.Width / 2, bounds.Bottom + 8)
            };

            foreach (Point point in outside)
            {
                Assert(OverlayInteraction.DecideMouseUp(MouseButtons.Right, point, bounds, true) ==
                    OverlayMouseAction.None, "outside right click requested exit at " + point);
            }
        }

        public static void RightDragFromOtherRegionDoesNotRequestExit()
        {
            Rectangle bounds = OverlayInteraction.GetMainUsageBounds(500, 28);
            Point center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);

            Assert(OverlayInteraction.DecideMouseUp(MouseButtons.Right, center, bounds, false) ==
                OverlayMouseAction.None, "right drag from another region requested exit");
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
