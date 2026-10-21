using Microsoft.Windows.ApplicationModel.Resources;

namespace EqApoTray.Services;

// Code-side lookup into Strings/<lang>/Resources.resw (XAML uses x:Uid for the
// same files). MRT Core picks the language from the Windows display-language
// list; anything other than zh-CN / en-US falls back to DefaultLanguage (en-US).
internal static class Strings
{
    private static readonly ResourceLoader Loader = new();

    public static string Get(string key) => Loader.GetString(key);
}
