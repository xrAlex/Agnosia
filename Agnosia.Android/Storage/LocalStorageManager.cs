using Android.Content;

namespace Agnosia.Android.Storage;

public sealed class LocalStorageManager
{
    private const string PreferencesName = "agnosia.preferences";

    private readonly ISharedPreferences _preferences;

    public LocalStorageManager(Context context)
    {
        _preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)
                       ?? throw new InvalidOperationException("Failed to create shared preferences storage.");
    }

    public void Remove(string key)
    {
        using var editor = _preferences.Edit();
        editor?.Remove(key)?.Apply();
    }

    public bool GetBoolean(string key, bool fallback = false)
    {
        return _preferences.GetBoolean(key, fallback);
    }

    public void SetBoolean(string key, bool value)
    {
        using var editor = _preferences.Edit();
        editor?.PutBoolean(key, value)?.Apply();
    }

    public void SetInt(string key, int value)
    {
        using var editor = _preferences.Edit();
        editor?.PutInt(key, value)?.Apply();
    }

    public long GetLong(string key, long fallback = 0)
    {
        return _preferences.GetLong(key, fallback);
    }

    public void SetLong(string key, long value)
    {
        using var editor = _preferences.Edit();
        editor?.PutLong(key, value)?.Apply();
    }

    public string? GetString(string key)
    {
        return _preferences.GetString(key, null);
    }

    public void SetString(string key, string? value)
    {
        using var editor = _preferences.Edit();
        editor?.PutString(key, value)?.Apply();
    }
}
