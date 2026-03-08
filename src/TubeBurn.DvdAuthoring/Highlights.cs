using TubeBurn.Domain;

namespace TubeBurn.DvdAuthoring;

public sealed class MenuHighlightPlanner
{
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
                    var row = index / 2;
                    var column = index % 2;

                    return new MenuButtonLayout(
                        $"btn-{page}-{index + 1}",
                        80 + (column * 280),
                        80 + (row * 140),
                        240,
                        120,
                        video.Title);
                }).ToList();

                layouts.Add(new ChannelMenuLayout(channel.Name, page++, buttons, channel.BannerImagePath));
            }
        }

        return layouts;
    }
}
