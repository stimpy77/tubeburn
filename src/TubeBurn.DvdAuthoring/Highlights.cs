using TubeBurn.Domain;

namespace TubeBurn.DvdAuthoring;

public sealed class MenuHighlightPlanner
{
    // DVD safe area: minimal margins to maximize usable width on 720-wide frame.
    private const int SafeLeft = 20;
    private const int SafeRight = 700;
    private const int SafeTop = 70;   // below title header
    private const int ButtonWidth = SafeRight - SafeLeft; // 680px
    private const int ButtonHeight = 46;
    private const int RowSpacing = 52;
    private const int NavButtonWidth = 140;
    private const int NavButtonHeight = 40;
    private const int MaxVideoRowsNtsc = 6;
    private const int MaxVideoRowsPal = 8;

    // Legacy constants for BuildLayouts backward compat
    private const int LegacyColumns = 2;
    private const int LegacyButtonWidth = 240;
    private const int LegacyButtonHeight = 120;
    private const int LegacyXSpacing = 280;
    private const int LegacyYSpacing = 140;

    public IReadOnlyList<ChannelMenuLayout> BuildLayouts(TubeBurnProject project, int buttonsPerPage = 4)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(buttonsPerPage);

        var layouts = new List<ChannelMenuLayout>();

        foreach (var channel in project.Channels)
        {
            var page = 1;

            foreach (var batch in channel.Videos.Chunk(buttonsPerPage))
            {
                var buttons = batch.Select((video, index) =>
                {
                    var row = index / LegacyColumns;
                    var column = index % LegacyColumns;

                    return new MenuButtonLayout(
                        $"btn-{page}-{index + 1}",
                        SafeLeft + (column * LegacyXSpacing),
                        SafeTop + (row * LegacyYSpacing),
                        LegacyButtonWidth,
                        LegacyButtonHeight,
                        video.Title);
                }).ToList();

                layouts.Add(new ChannelMenuLayout(channel.Name, page++, buttons, channel.BannerImagePath));
            }
        }

        return layouts;
    }

    /// <summary>
    /// Builds video-select menu pages for a channel with full navigation and commands.
    /// Layout: full-width rows stacked vertically, one row per video.
    /// </summary>
    public List<MenuPage> BuildVideoSelectPages(
        ChannelProject channel, int vtsNumber, int videosPerPage = 0, bool isMultiChannel = false,
        bool useChapterNavigation = false)
    {
        ArgumentNullException.ThrowIfNull(channel);

        // Default videos per page based on safe area
        if (videosPerPage <= 0)
            videosPerPage = MaxVideoRowsNtsc; // conservative default; PAL could fit more

        var pages = new List<MenuPage>();
        var batches = channel.Videos.Chunk(videosPerPage).ToList();

        for (var pageIndex = 0; pageIndex < batches.Count; pageIndex++)
        {
            var batch = batches[pageIndex];
            var buttons = new List<MenuButton>();
            var pageNumber = pageIndex + 1;

            // Video buttons: full-width rows stacked vertically
            for (var i = 0; i < batch.Length; i++)
            {
                var video = batch[i];
                var titleIndex = pageIndex * videosPerPage + i + 1; // 1-based title number
                var y = SafeTop + i * RowSpacing;

                // Multi-chapter mode: JumpVtsPtt(title 1, part N) — chapters within single title.
                // Multi-title mode: JumpVtsTt(title N) — separate titles.
                var buttonCmd = useChapterNavigation
                    ? new DvdButtonCommand(DvdButtonCommandKind.JumpVtsPtt, titleIndex)
                    : new DvdButtonCommand(DvdButtonCommandKind.JumpVtsTt, titleIndex);

                buttons.Add(new MenuButton(
                    $"video-{pageNumber}-{i + 1}",
                    SafeLeft, y, ButtonWidth, ButtonHeight,
                    video.Title,
                    default!, // navigation set below
                    buttonCmd,
                    ThumbnailPath: video.ThumbnailPath));
            }

            // Navigation buttons at bottom
            var navY = SafeTop + batch.Length * RowSpacing + 10;
            var hasMultiplePages = batches.Count > 1;
            var hasPrev = pageIndex > 0;
            var hasNext = pageIndex < batches.Count - 1;

            // Position nav buttons: spread across bottom row
            var navButtons = new List<(string id, string label, DvdButtonCommand cmd)>();

            if (hasPrev)
                navButtons.Add(($"prev-{pageNumber}", "< Prev",
                    new DvdButtonCommand(DvdButtonCommandKind.LinkPgcn, pageNumber - 1)));

            // Multi-channel: Back returns to VMGM channel-select menu
            // Single-channel: no Back button (no parent menu, Exit asserts VTSTitle domain)
            if (isMultiChannel)
            {
                navButtons.Add(($"back-{pageNumber}", "Back",
                    new DvdButtonCommand(DvdButtonCommandKind.JumpSsVmgm, 0)));
            }

            if (hasNext)
                navButtons.Add(($"next-{pageNumber}", "Next >",
                    new DvdButtonCommand(DvdButtonCommandKind.LinkPgcn, pageIndex + 2)));

            var navSpacing = ButtonWidth / Math.Max(navButtons.Count, 1);
            for (var n = 0; n < navButtons.Count; n++)
            {
                var (id, label, cmd) = navButtons[n];
                buttons.Add(new MenuButton(
                    id,
                    SafeLeft + n * navSpacing, navY, NavButtonWidth, NavButtonHeight,
                    label, default!, cmd));
            }

            buttons = AssignNavigation(buttons);

            pages.Add(new MenuPage(
                channel.Name, pageNumber, buttons,
                channel.BannerImagePath, MenuPageType.VideoSelect,
                AvatarImagePath: channel.AvatarImagePath));
        }

        return pages;
    }

    /// <summary>
    /// Builds a channel-select menu page for multi-channel projects.
    /// Layout: full-width rows stacked vertically, one row per channel.
    /// </summary>
    public MenuPage BuildChannelSelectPage(IReadOnlyList<ChannelProject> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);

        var buttons = new List<MenuButton>();

        for (var i = 0; i < channels.Count; i++)
        {
            var y = SafeTop + i * RowSpacing;
            var vtsNumber = i + 1;

            buttons.Add(new MenuButton(
                $"channel-{i + 1}",
                SafeLeft, y, ButtonWidth, ButtonHeight,
                channels[i].Name,
                default!,
                new DvdButtonCommand(DvdButtonCommandKind.JumpSsVtsm, vtsNumber),
                ThumbnailPath: channels[i].AvatarImagePath));
        }

        buttons = AssignNavigation(buttons);

        return new MenuPage(
            "Channel Select", 1, buttons,
            "", MenuPageType.ChannelSelect);
    }

    /// <summary>
    /// Assigns D-pad navigation to all buttons.
    /// Single-column layout: up/down moves between rows, left/right wraps to self.
    /// </summary>
    private static List<MenuButton> AssignNavigation(List<MenuButton> buttons)
    {
        var result = new List<MenuButton>();

        for (var i = 0; i < buttons.Count; i++)
        {
            var btn = buttons[i];
            var btnNum = i + 1; // 1-based

            var up = FindNearest(buttons, i, 0, -1);
            var down = FindNearest(buttons, i, 0, 1);
            var left = FindNearest(buttons, i, -1, 0);
            var right = FindNearest(buttons, i, 1, 0);

            var nav = new ButtonNavigation(
                up >= 0 ? up + 1 : btnNum,
                down >= 0 ? down + 1 : btnNum,
                left >= 0 ? left + 1 : btnNum,
                right >= 0 ? right + 1 : btnNum);

            result.Add(btn with { Navigation = nav });
        }

        return result;
    }

    private static int FindNearest(List<MenuButton> buttons, int fromIndex, int dx, int dy)
    {
        var from = buttons[fromIndex];
        var fromCx = from.X + from.Width / 2;
        var fromCy = from.Y + from.Height / 2;

        var bestIndex = -1;
        var bestDist = int.MaxValue;

        for (var i = 0; i < buttons.Count; i++)
        {
            if (i == fromIndex) continue;

            var to = buttons[i];
            var toCx = to.X + to.Width / 2;
            var toCy = to.Y + to.Height / 2;

            var ddx = toCx - fromCx;
            var ddy = toCy - fromCy;

            if (dx < 0 && ddx >= 0) continue;
            if (dx > 0 && ddx <= 0) continue;
            if (dy < 0 && ddy >= 0) continue;
            if (dy > 0 && ddy <= 0) continue;

            var dist = ddx * ddx + ddy * ddy;
            if (dist < bestDist)
            {
                bestDist = dist;
                bestIndex = i;
            }
        }

        return bestIndex;
    }
}
