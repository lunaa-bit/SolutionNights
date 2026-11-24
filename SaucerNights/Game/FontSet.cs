using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using SaucerNights.Ui;

namespace SaucerNights.Game;

/// <summary>
/// Fonts baked at the sizes the UI draws at: the game's TrumpGothic for headings and banners, a classic serif (when
/// Windows has Georgia) for card ranks, and a small Dalamud font for captions.
/// </summary>
internal sealed class FontSet : IDisposable
{
    private const string Georgia = @"C:\Windows\Fonts\georgiab.ttf";

    private readonly IFontHandle _small;
    private readonly IFontHandle _heading;
    private readonly IFontHandle _headingSmall;
    private readonly IFontHandle _display;
    private readonly IFontHandle _card;
    private readonly IFontHandle _cardSmall;
    private readonly Dictionary<IFontHandle, ImFontPtr> _frameCache = new();
    private int _frame = -1;

    public FontSet(IUiBuilder ui)
    {
        var atlas = ui.FontAtlas;
        _small = atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk => tk.AddDalamudDefaultFont(13)));
        _heading = atlas.NewGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.TrumpGothic34));
        _headingSmall = atlas.NewGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.TrumpGothic23));
        _display = atlas.NewGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.TrumpGothic68));
        _card = CardFont(atlas, 48, GameFontFamilyAndSize.TrumpGothic34);
        _cardSmall = CardFont(atlas, 24, GameFontFamilyAndSize.TrumpGothic23);
    }

    private static IFontHandle CardFont(IFontAtlas atlas, float px, GameFontFamilyAndSize fallback)
    {
        if (!File.Exists(Georgia)) return atlas.NewGameFontHandle(new GameFontStyle(fallback));
        return atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
        {
            try
            {
                tk.AddFontFromFile(Georgia, new SafeFontConfig { SizePx = px });
            }
            catch
            {
                tk.AddDalamudDefaultFont(px);
            }
        }));
    }

    /// <summary><see cref="UiEnv.FontProvider"/>: the closest baked font for a role and pixel size.</summary>
    public ImFontPtr? Get(FontRole role, float size)
    {
        var scale = UiEnv.Scale;
        var handle = role switch
        {
            FontRole.Body when size > 0 && size <= 14.5f * scale => _small,
            FontRole.Body => null,
            FontRole.Heading when size > 40 * scale => _display,
            FontRole.Heading when size > 0 && size <= 23.5f * scale => _headingSmall,
            FontRole.Heading => _heading,
            FontRole.Display => _display,
            FontRole.Card when size > 0 && size <= 25 * scale => _cardSmall,
            FontRole.Card => _card,
            _ => null,
        };
        if (handle == null || !handle.Available) return null;

        // The pointer is stable for the frame; fetch it once per frame per font.
        var frame = ImGui.GetFrameCount();
        if (frame != _frame)
        {
            _frame = frame;
            _frameCache.Clear();
        }
        if (_frameCache.TryGetValue(handle, out var cached)) return cached;
        using (handle.Push())
        {
            var font = ImGui.GetFont();
            _frameCache[handle] = font;
            return font;
        }
    }

    public void Dispose()
    {
        _small.Dispose();
        _heading.Dispose();
        _headingSmall.Dispose();
        _display.Dispose();
        _card.Dispose();
        _cardSmall.Dispose();
    }
}
