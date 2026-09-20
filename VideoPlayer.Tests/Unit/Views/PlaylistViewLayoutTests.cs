using System.Windows.Controls;
using VideoPlayer.Views;
using Xunit;

namespace VideoPlayer.Tests.Unit.Views;

public sealed class PlaylistViewLayoutTests
{
    [StaFact]
    public void Layout_HasCountHeaderThumbnailAndIconActions()
    {
        var view = new PlaylistView();

        Assert.IsType<TextBlock>(view.FindName("PlaylistCountText"));
        Assert.NotNull(Assert.IsType<ListBox>(view.FindName("PlaylistListBox")).ItemTemplate);
        Assert.IsType<Button>(view.FindName("DeletePlaylistItemButton"));
        Assert.IsType<Button>(view.FindName("ClearPlaylistButton"));
    }
}