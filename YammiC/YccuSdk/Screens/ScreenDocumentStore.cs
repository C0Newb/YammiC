using CNCO.YccuSdk.Platform;

namespace CNCO.YccuSdk.Screens;

/// <summary>
/// Saves each target's <see cref="ScreenDocument"/> (as AXAML text) in the platform key-value store. With nothing saved it
/// uses the old JSON layout if there is one (migrated), else the built-in screen. Unreadable saved text is ignored.
/// </summary>
public sealed class ScreenDocumentStore
{
    private readonly IYmcKeyValueStore _storage;
    private readonly ScreenLayoutStore _legacy;

    public ScreenDocumentStore(IYmcKeyValueStore storage)
    {
        _storage = storage;
        _legacy = new ScreenLayoutStore(storage);
    }

    private static string Key(ScreenTarget target) => target == ScreenTarget.Navigation ? "screen_doc_navigation" : "screen_doc_vpb";

    public ScreenDocument Load(ScreenTarget target)
    {
        var text = _storage.GetString(Key(target));
        if (!string.IsNullOrWhiteSpace(text))
        {
            try
            {
                var document = ScreenDocument.Parse(text);
                document.Target = target;
                if (document.Pages.All(p => ScreenXamlValidator.Validate(p.Xaml).Count == 0)) return document;
            }
            catch (ScreenDocumentException)
            {
                // Fall through to the old layout / the default.
            }
        }

        // An old JSON layout is converted; with nothing saved the built-in screen is used.
        var old = _legacy.LoadSaved(target);
        return old != null ? ScreenLayoutMigration.ToDocument(old) : ScreenDefaults.Document(target);
    }

    public void Save(ScreenTarget target, ScreenDocument document)
    {
        document.Target = target;
        _storage.SetString(Key(target), document.ToAxaml());
    }

    /// <summary>
    /// Forgets the saved screen (and any old layout) so <see cref="Load"/> returns the built-in one.
    /// </summary>
    public void Reset(ScreenTarget target)
    {
        _storage.SetString(Key(target), "");
        _legacy.Reset(target);
    }
}
