namespace MadEngine.Utilities;

public static class UriUtilities
{
    public static Uri RelativeUri(string basePath, string path)
    {
        return new Uri(System.IO.Path.Combine(basePath, path), UriKind.Absolute);
    }

    public static Uri RelativeUri(string path)
    {
        return new Uri(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path), UriKind.Absolute);
    }
}